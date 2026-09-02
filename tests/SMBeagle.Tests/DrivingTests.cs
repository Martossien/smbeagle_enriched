using System.Text.Json;

namespace SMBeagle.Tests;

/// <summary>Pilotage par docia : codes de retour, --progress-json, --manifest, --preserve-access-time.</summary>
public class DrivingTests
{
    static List<JsonDocument> JsonLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).Select(l => JsonDocument.Parse(l)).ToList();

    [Fact]
    public void Code_0_scan_reussi()
    {
        string csv = Path.Combine(Repo.TempDir(), "scan.csv");
        Assert.Equal(0, Repo.Run("--local-path", Repo.Fixtures, "-c", csv, "-q").ExitCode);
    }

    [Fact]
    public void Code_2_arguments_invalides()
    {
        Assert.Equal(2, Repo.Run("--option-inconnue").ExitCode);
        Assert.Equal(2, Repo.Run("--local-path", Repo.Fixtures).ExitCode); // aucune sortie (-c / -e)
        Assert.Equal(2, Repo.Run("--local-path", Repo.Fixtures, "-c", Path.Combine(Repo.TempDir(), "x.csv"), "-u", "seul").ExitCode);
        Assert.Equal(2, Repo.Run("--local-path", Repo.Fixtures, "-c", Path.Combine(Repo.TempDir(), "x.csv"), "-a", "11").ExitCode);
        Assert.Equal(2, Repo.Run("--local-path", Repo.Fixtures, "-c", Path.Combine(Repo.TempDir(), "x.csv"), "-g", "--file-pattern", "[invalide").ExitCode);
        string tmp = Repo.TempDir();
        Assert.Equal(2, Repo.Run("--local-path", Path.Combine(tmp, "inexistant"), "-c", Path.Combine(tmp, "a.csv"), "-q").ExitCode);
        Assert.Equal(0, Repo.Run("--help").ExitCode);
        Assert.Equal(0, Repo.Run("--version").ExitCode);
    }

    [Fact]
    public void Code_3_aucune_cible_ou_rien_trouve()
    {
        // Un chemin inexistant vaut 2 (arguments) et non 3 : voir Code_2_arguments_invalides.
        string tmp = Repo.TempDir();
        string vide = Path.Combine(tmp, "vide");
        Directory.CreateDirectory(vide);
        Assert.Equal(3, Repo.Run("--local-path", vide, "-c", Path.Combine(tmp, "b.csv"), "-q").ExitCode);
    }

    [Fact]
    public void Code_1_erreur_d_execution()
    {
        string csvDansDossierInexistant = Path.Combine(Repo.TempDir(), "absent", "scan.csv");
        var run = Repo.Run("--local-path", Repo.Fixtures, "-c", csvDansDossierInexistant, "-q", "--progress-json");
        Assert.Equal(1, run.ExitCode);
        var events = JsonLines(run.Stdout);
        Assert.Equal("error", events[^1].RootElement.GetProperty("event").GetString());
        Assert.Contains("CSV", events[^1].RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Progress_json_stdout_reserve_aux_lignes_json_et_sequence_complete()
    {
        string csv = Path.Combine(Repo.TempDir(), "scan.csv");
        var run = Repo.Run("--local-path", Repo.Fixtures, "-c", csv, "--progress-json", "--fasthash");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("AUDIT COMPLETE", run.Stderr);
        var events = JsonLines(run.Stdout);
        Assert.True(events.Count >= 3, run.Stdout);
        var first = events[0].RootElement;
        Assert.Equal("progress", first.GetProperty("event").GetString());
        Assert.Equal("files", first.GetProperty("stage").GetString());
        foreach (var name in new[] { "hosts", "shares", "files", "elapsed_s" })
            Assert.True(first.TryGetProperty(name, out _), name);
        var stages = events.Where(e => e.RootElement.GetProperty("event").GetString() == "progress")
            .Select(e => e.RootElement.GetProperty("stage").GetString()).ToList();
        Assert.Equal(new[] { "files", "writing" }, stages.Distinct());
        var done = events[^1].RootElement;
        Assert.Equal("done", done.GetProperty("event").GetString());
        Assert.Equal(7, done.GetProperty("files").GetInt64());
        Assert.Equal(Path.GetFullPath(csv), done.GetProperty("csv").GetString());
        Assert.True(done.GetProperty("elapsed_s").GetDouble() >= 0);
    }

    [Fact]
    public void Progress_json_erreur_d_arguments_en_json()
    {
        var run = Repo.Run("--option-inconnue", "--progress-json");
        Assert.Equal(2, run.ExitCode);
        var events = JsonLines(run.Stdout);
        Assert.Single(events);
        Assert.Equal("error", events[0].RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public void Sans_progress_json_stdout_reste_humain()
    {
        string csv = Path.Combine(Repo.TempDir(), "scan.csv");
        var run = Repo.Run("--local-path", Repo.Fixtures, "-c", csv, "-q");
        Assert.DoesNotContain("{\"event\"", run.Stdout);
        Assert.Contains("AUDIT COMPLETE", run.Stdout);
    }

    [Fact]
    public void Manifeste_complet_options_effectives_et_mot_de_passe_masque()
    {
        string tmp = Repo.TempDir();
        string csv = Path.Combine(tmp, "scan.csv");
        string manifest = Path.Combine(tmp, "scan.json");
        var run = Repo.Run("--local-path", Repo.Fixtures, "-c", csv, "-q", "--manifest", manifest, "--sizefile", "--fasthash", "-u", "bob", "-p", "secret");
        Assert.Equal(0, run.ExitCode);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = doc.RootElement;
        Assert.Equal(new[] { "version", "started_at", "finished_at", "options", "targets", "skipped", "counts", "unreadable_directories", "csv", "columns" },
            root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Empty(root.GetProperty("skipped").EnumerateArray());
        Assert.Matches(@"^\d+\.\d+\.\d+$", root.GetProperty("version").GetString());
        Assert.True(DateTimeOffset.Parse(root.GetProperty("started_at").GetString()!) <= DateTimeOffset.Parse(root.GetProperty("finished_at").GetString()!));
        var options = root.GetProperty("options");
        Assert.True(options.GetProperty("sizefile").GetBoolean());
        Assert.True(options.GetProperty("fasthash").GetBoolean());
        Assert.False(options.GetProperty("file-signature").GetBoolean());
        Assert.Equal("bob", options.GetProperty("username").GetString());
        Assert.Equal("***", options.GetProperty("password").GetString());
        Assert.Equal("9200", options.GetProperty("elasticsearch-port").GetString());
        Assert.Equal(new[] { Path.GetFullPath(Repo.Fixtures) }, root.GetProperty("targets").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(7, root.GetProperty("counts").GetProperty("files").GetInt64());
        Assert.Equal(0, root.GetProperty("counts").GetProperty("hosts").GetInt64());
        Assert.Equal(0, root.GetProperty("counts").GetProperty("dirs_unreadable").GetInt64());
        Assert.Empty(root.GetProperty("unreadable_directories").EnumerateArray());
        Assert.Equal(Path.GetFullPath(csv), root.GetProperty("csv").GetString());
        Assert.Equal(Csv.Header, root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal(7, Csv.ReadRows(csv).Count);
    }

    [Fact]
    public void Preserve_access_time_restaure_la_date_d_acces_apres_lecture_du_contenu()
    {
        string dir = Repo.TempDir();
        string file = Path.Combine(dir, "ancien.txt");
        File.WriteAllText(file, "contenu lu pour le hash et la signature");
        var ancienne = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local);
        File.SetLastAccessTime(file, ancienne);
        Assert.Equal(ancienne, File.GetLastAccessTime(file));

        string csv = Path.Combine(Repo.TempDir(), "scan.csv"); // hors du dossier scanné
        var run = Repo.Run("--local-path", dir, "-c", csv, "-q", "--access-time", "--fasthash", "--file-signature", "--preserve-access-time");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ancienne, File.GetLastAccessTime(file));
        var row = Csv.ReadRows(csv).Single();
        Assert.Equal("02/01/2020 03:04:05", row["AccessTime"]);
        Assert.Matches("^[0-9a-f]{16}$", row["FastHash"]);
    }

    [Fact]
    public void Access_time_lu_avant_la_lecture_du_contenu_meme_sans_preservation()
    {
        string dir = Repo.TempDir();
        string file = Path.Combine(dir, "ancien.txt");
        File.WriteAllText(file, "contenu lu pour le hash");
        var ancienne = new DateTime(2019, 6, 7, 8, 9, 10, DateTimeKind.Local);
        File.SetLastAccessTime(file, ancienne);
        string csv = Path.Combine(Repo.TempDir(), "scan.csv"); // hors du dossier scanné
        var run = Repo.Run("--local-path", dir, "-c", csv, "-q", "--access-time", "--fasthash");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("07/06/2019 08:09:10", Csv.ReadRows(csv).Single()["AccessTime"]);
    }
}
