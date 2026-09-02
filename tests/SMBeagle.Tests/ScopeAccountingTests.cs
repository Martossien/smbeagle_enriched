using System.Text.Json;

namespace SMBeagle.Tests;

/// <summary>
/// Ce que le scan dit de son propre périmètre : cibles, sous-répertoires illisibles,
/// jonctions. Trois faits qu'un audit ne doit jamais taire — ils décident si l'inventaire
/// peut justifier des suppressions.
/// </summary>
public class ScopeAccountingTests
{
    static (Repo.RunResult run, JsonElement manifest, List<Dictionary<string, string>> rows) Scan(string root, string tmp)
    {
        string csv = Path.Combine(tmp, "scan.csv");
        string manifestPath = Path.Combine(tmp, "scan.json");
        var run = Repo.Run("--local-path", root, "-c", csv, "-q", "--sizefile", "--manifest", manifestPath);
        var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement;
        return (run, manifest, File.Exists(csv) ? Csv.ReadRows(csv) : new());
    }

    static string TreeWithSubdirectories(string tmp, int subdirs)
    {
        string root = Path.Combine(tmp, "racine");
        for (int i = 0; i < subdirs; i++)
        {
            string dir = Path.Combine(root, $"service_{i:D2}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"note_{i:D2}.txt"), "contenu");
        }
        return root;
    }

    /// <summary>
    /// Une racine de plus de 20 sous-répertoires est découpée en lots pour la sortie
    /// (SplitLargeDirectories). Les cibles du manifeste étaient lues APRÈS ce découpage,
    /// comme « les répertoires sans parent » : liste vide, et le scan sortait en code 3
    /// « aucun fichier » avec toutes ses lignes écrites — sur tout partage réel, donc.
    /// </summary>
    [Fact]
    public void Racine_de_25_sous_dossiers_reste_la_cible_et_sort_en_code_0()
    {
        string tmp = Repo.TempDir();
        string root = TreeWithSubdirectories(tmp, 25);
        var (run, manifest, rows) = Scan(root, tmp);
        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        Assert.Equal(25, rows.Count);
        Assert.Equal(new[] { root }, manifest.GetProperty("targets").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(25, manifest.GetProperty("counts").GetProperty("files").GetInt64());
        Assert.Equal(0, manifest.GetProperty("counts").GetProperty("dirs_unreadable").GetInt64());
        Assert.DoesNotContain("No valid local path", run.Stdout + run.Stderr);
    }

    /// <summary>
    /// Un sous-répertoire fermé par ACL disparaissait sans un mot (message en -v seulement) :
    /// l'inventaire se présentait comme complet. Il est compté, listé dans le manifeste et
    /// annoncé sur stderr ; le reste du scan continue.
    /// </summary>
    [Fact]
    public void Sous_dossier_illisible_compte_liste_et_annonce()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
            return; // les droits POSIX ne bloquent pas root ; Windows a d'autres ACL
        string tmp = Repo.TempDir();
        string root = TreeWithSubdirectories(tmp, 3);
        string closed = Path.Combine(root, "service_01");
        File.SetUnixFileMode(closed, UnixFileMode.None);
        try
        {
            var (run, manifest, rows) = Scan(root, tmp);
            Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, manifest.GetProperty("counts").GetProperty("dirs_unreadable").GetInt64());
            Assert.Equal(new[] { closed }, manifest.GetProperty("unreadable_directories").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("répertoire(s) non lu(s)", run.Stderr);
        }
        finally
        {
            File.SetUnixFileMode(closed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Un lien de répertoire vers un ancêtre faisait boucler l'énumération jusqu'au
    /// « chemin trop long ». Les points de réanalyse sont ignorés et comptés ; le contenu
    /// réel est scanné par son vrai chemin, une seule fois.
    /// </summary>
    [Fact]
    public void Lien_vers_le_parent_est_ignore_et_compte()
    {
        if (!OperatingSystem.IsLinux())
            return; // créer un lien symbolique de répertoire exige un privilège sous Windows
        string tmp = Repo.TempDir();
        string root = TreeWithSubdirectories(tmp, 2);
        Directory.CreateSymbolicLink(Path.Combine(root, "service_00", "boucle"), root);
        var (run, manifest, rows) = Scan(root, tmp);
        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, manifest.GetProperty("counts").GetProperty("reparse_points_skipped").GetInt64());
    }
}
