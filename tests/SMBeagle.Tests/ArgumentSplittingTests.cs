using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace SMBeagle.Tests;

/// <summary>
/// Chemins contenant une espace : sous Windows, `--local-path D:\mes fichiers` sans
/// guillemets arrive en deux argv distincts. Chaque appel ci-dessous passe par
/// ProcessStartInfo.ArgumentList (un élément = un argv), ce qui reproduit exactement
/// la coupure. Un fragment surnuméraire doit valoir 2, jamais un scan silencieux.
/// </summary>
public class ArgumentSplittingTests
{
    static string DossierAvecFichier(string racine, string nom, string fichier = "note.txt")
    {
        string dir = Path.Combine(racine, nom);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fichier), "contenu de " + fichier);
        return dir;
    }

    /// <summary>
    /// Rend un répertoire non énumérable pour l'utilisateur courant, et vérifie que le refus
    /// est bien effectif : root (ou un administrateur Windows) passe outre, auquel cas le
    /// test appelant n'a rien à prouver et s'arrête.
    /// </summary>
    static bool RendreIllisible(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new DirectoryInfo(dir);
                var acl = info.GetAccessControl();
                acl.AddAccessRule(new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User!,
                    FileSystemRights.ListDirectory | FileSystemRights.ReadData | FileSystemRights.Traverse,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                info.SetAccessControl(acl);
            }
            else
            {
                File.SetUnixFileMode(dir, UnixFileMode.None);
            }
        }
        catch (Exception)
        {
            return false;
        }
        try
        {
            _ = Directory.EnumerateFileSystemEntries(dir).FirstOrDefault();
            return false; // le refus n'a pas pris : rien à tester
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void RendreLisible(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new DirectoryInfo(dir);
                var acl = info.GetAccessControl();
                acl.RemoveAccessRuleAll(new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User!,
                    FileSystemRights.ListDirectory | FileSystemRights.ReadData | FileSystemRights.Traverse,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                info.SetAccessControl(acl);
            }
            else
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception)
        {
            // best effort : le répertoire temporaire n'est pas nettoyé de toute façon
        }
    }

    [Fact]
    public void Dossier_avec_espace_correctement_guillemete_scanne_normalement()
    {
        string tmp = Repo.TempDir();
        string cible = DossierAvecFichier(tmp, "mes fichiers");
        string csv = Path.Combine(tmp, "scan.csv");
        var run = Repo.Run("--local-path", cible, "-c", csv, "-q", "--sizefile");
        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        var rows = Csv.ReadRows(csv);
        Assert.Equal("note.txt", Assert.Single(rows)["Name"]);
        Assert.Equal(cible, rows[0]["UNCDirectory"]);
    }

    [Fact]
    public void Chemin_coupe_dont_le_premier_fragment_existe_echoue_au_lieu_de_scanner_la_mauvaise_cible()
    {
        string tmp = Repo.TempDir();
        DossierAvecFichier(tmp, "mes", "mauvaise-cible.txt"); // le fragment 'mes' EXISTE
        DossierAvecFichier(tmp, "mes fichiers", "bonne-cible.txt");
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.Run("--local-path", Path.Combine(tmp, "mes"), "fichiers", "-c", csv, "-q");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--local-path is not an absolute path: 'fichiers'", run.Stdout);
        Assert.Contains("must be quoted", run.Stdout);
        Assert.Contains("do not end a quoted path with a backslash", run.Stdout);
        Assert.False(File.Exists(csv), "aucun CSV ne doit être écrit pour la mauvaise cible");
    }

    /// <summary>
    /// Le fragment surnuméraire est un chemin relatif : s'il existe par hasard dans le
    /// répertoire courant (sous Windows : Documents, Downloads, Bureau...), Path.GetFullPath
    /// le résolvait contre ce répertoire et SMBeagle scannait le mauvais dossier en code 0.
    /// </summary>
    [Fact]
    public void Fragment_relatif_existant_dans_le_repertoire_courant_ne_doit_pas_etre_scanne()
    {
        string tmp = Repo.TempDir();
        DossierAvecFichier(tmp, "mes", "mauvaise-cible.txt");
        DossierAvecFichier(tmp, "mes fichiers", "bonne-cible.txt");
        string cwd = Path.Combine(tmp, "cwd");
        DossierAvecFichier(cwd, "fichiers", "cible-du-repertoire-courant.txt"); // cwd/fichiers EXISTE
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.RunIn(cwd, "--local-path", Path.Combine(tmp, "mes"), "fichiers", "-c", csv, "-q");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--local-path is not an absolute path: 'fichiers'", run.Stdout);
        Assert.Contains("resolved against the current directory", run.Stdout);
        Assert.False(File.Exists(csv), "aucun CSV ne doit être écrit quand un chemin est relatif");
    }

    /// <summary>
    /// Régression du correctif précédent : Directory.Exists rend false pour un dossier
    /// inaccessible comme pour un dossier absent. Un partage fermé par ACL doit rester un
    /// « rien trouvé » (code 3, toléré par docia) et non une erreur d'arguments (code 2).
    /// </summary>
    [Fact]
    public void Chemin_refuse_vaut_3_avec_un_message_distinct_de_not_found()
    {
        string tmp = Repo.TempDir();
        string ferme = DossierAvecFichier(tmp, "ferme", "secret.txt");
        string csv = Path.Combine(tmp, "scan.csv");
        if (!RendreIllisible(ferme))
            return; // exécuté en root / administrateur : le refus est inopérant
        try
        {
            var run = Repo.Run("--local-path", ferme, "-c", csv, "-q");

            Assert.Equal(3, run.ExitCode);
            Assert.Contains("access denied", run.Stdout);
            Assert.DoesNotContain("not found", run.Stdout);
            Assert.True(File.Exists(csv), "le CSV doit exister même en code 3 (docia le relit)");
        }
        finally
        {
            RendreLisible(ferme);
        }
    }

    /// <summary>
    /// Plusieurs --local-path dont un seul est refusé : le scan continue sur les autres,
    /// mais il ne sort PAS en 0 et le manifeste nomme ce qui manque.
    ///
    /// Ce test attendait le code 0 : c'est exactement le défaut relevé par l'audit du
    /// 01/09. Un partage entier sortait de l'audit sur une ligne d'avertissement noyée
    /// dans la sortie, et rien en aval — ni le CSV, ni la base, ni le rapport remis à
    /// la direction — ne disait qu'il manquait. Un outil qui sert à décider de
    /// suppressions doit dire « je n'ai pas tout vu ».
    /// </summary>
    [Fact]
    public void Un_chemin_refuse_parmi_d_autres_scanne_les_autres_mais_sort_en_4()
    {
        string tmp = Repo.TempDir();
        string ferme = DossierAvecFichier(tmp, "ferme", "secret.txt");
        string ouvert = DossierAvecFichier(tmp, "ouvert", "visible.txt");
        string csv = Path.Combine(tmp, "scan.csv");
        string manifeste = Path.Combine(tmp, "scan.json");
        if (!RendreIllisible(ferme))
            return;
        try
        {
            var run = Repo.Run("--local-path", ferme, ouvert, "-c", csv, "-q", "--manifest", manifeste);

            Assert.True(run.ExitCode == 4, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
            Assert.Contains("access denied", run.Stdout);
            // Le CSV reste bon et exploitable : c'est le périmètre qui est amputé.
            Assert.Equal("visible.txt", Assert.Single(Csv.ReadRows(csv))["Name"]);

            using var doc = JsonDocument.Parse(File.ReadAllText(manifeste));
            var skipped = doc.RootElement.GetProperty("skipped").EnumerateArray().Select(e => e.GetString()).ToList();
            var targets = doc.RootElement.GetProperty("targets").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Equal(new[] { ferme }, skipped);
            Assert.Equal(new[] { ouvert }, targets);
        }
        finally
        {
            RendreLisible(ferme);
        }
    }

    /// <summary>Périmètre intact : « skipped » est vide et le code reste 0.</summary>
    [Fact]
    public void Sans_chemin_refuse_le_manifeste_ne_signale_rien()
    {
        string tmp = Repo.TempDir();
        string ouvert = DossierAvecFichier(tmp, "ouvert", "visible.txt");
        string csv = Path.Combine(tmp, "scan.csv");
        string manifeste = Path.Combine(tmp, "scan.json");

        var run = Repo.Run("--local-path", ouvert, "-c", csv, "-q", "--manifest", manifeste);

        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifeste));
        Assert.Empty(doc.RootElement.GetProperty("skipped").EnumerateArray());
    }

    [Fact]
    public void Chemin_relatif_explicite_est_refuse_comme_non_absolu()
    {
        string tmp = Repo.TempDir();
        DossierAvecFichier(tmp, "data", "a.txt");
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.RunIn(tmp, "--local-path", Path.Combine("..", "data"), "-c", csv, "-q");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--local-path is not an absolute path", run.Stdout);
        Assert.Contains("resolved against the current directory", run.Stdout);
        Assert.False(File.Exists(csv));
    }

    [Fact]
    public void Local_path_vide_est_refuse_avec_un_message_explicite()
    {
        string tmp = Repo.TempDir();
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.Run("--local-path", "", "-c", csv, "-q");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--local-path needs a directory, an empty value was given", run.Stdout);
        Assert.DoesNotContain("not found", run.Stdout);
        Assert.False(File.Exists(csv));
    }

    [Fact]
    public void Chemin_coupe_signale_l_erreur_en_json_avec_progress_json()
    {
        string tmp = Repo.TempDir();
        var run = Repo.Run("--local-path", Path.Combine(tmp, "absent"), "fragment", "-c", Path.Combine(tmp, "scan.csv"), "-q", "--progress-json");
        Assert.Equal(2, run.ExitCode);
        var events = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l.TrimEnd('\r')).RootElement).ToList();
        var last = events[^1];
        Assert.Equal("error", last.GetProperty("event").GetString());
        Assert.Contains("directory not found", last.GetProperty("message").GetString());
    }

    [Fact]
    public void Token_surnumeraire_apres_c_echoue_sans_creer_de_fichier_tronque()
    {
        string tmp = Repo.TempDir();
        string cible = DossierAvecFichier(tmp, "donnees");
        string tronque = Path.Combine(tmp, "sortie");

        var run = Repo.Run("--local-path", cible, "-c", tronque, "C.csv", "-q");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("unexpected extra argument(s): 'C.csv'", run.Stdout);
        Assert.Contains("must be quoted", run.Stdout);
        Assert.False(File.Exists(tronque), "le nom tronqué ne doit pas être créé");
        Assert.False(File.Exists(tronque + " C.csv"));
    }

    [Fact]
    public void Csv_et_manifeste_avec_des_espaces_gardent_leur_nom_exact()
    {
        string tmp = Repo.TempDir();
        string cible = DossierAvecFichier(tmp, "partage commun");
        string csv = Path.Combine(tmp, "sortie du jour.csv");
        string manifest = Path.Combine(tmp, "manifeste du jour.json");

        var run = Repo.Run("--local-path", cible, "-c", csv, "--manifest", manifest, "-q");

        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        Assert.True(File.Exists(csv), csv);
        Assert.True(File.Exists(manifest), manifest);
        Assert.Single(Csv.ReadRows(csv));
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.Equal(Path.GetFullPath(csv), doc.RootElement.GetProperty("csv").GetString());
    }

    [Fact]
    public void Dossier_contenant_une_virgule_scanne_normalement()
    {
        string tmp = Repo.TempDir();
        string cible = DossierAvecFichier(tmp, "archives 2019, 2020");
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.Run("--local-path", cible, "-c", csv, "-q");

        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        Assert.Equal(cible, Assert.Single(Csv.ReadRows(csv))["UNCDirectory"]);
    }

    [Fact]
    public void Plusieurs_local_path_valides_restent_acceptes()
    {
        string tmp = Repo.TempDir();
        string a = DossierAvecFichier(tmp, "premier", "a.txt");
        string b = DossierAvecFichier(tmp, "second", "b.txt");
        string csv = Path.Combine(tmp, "scan.csv");

        var run = Repo.Run("--local-path", a, b, "-c", csv, "-q");

        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        Assert.Equal(new[] { "a.txt", "b.txt" }, Csv.ReadRows(csv).Select(r => r["Name"]).OrderBy(n => n, StringComparer.Ordinal));
    }
}
