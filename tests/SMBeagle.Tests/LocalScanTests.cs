namespace SMBeagle.Tests;

/// <summary>Tests bout en bout : l'exécutable réel en mode --local-path sur tests/fixtures.</summary>
public class LocalScanTests
{
    static readonly string[] MetadataOptions =
        { "--sizefile", "--access-time", "--fileattributes", "--ownerfile", "--fasthash", "--file-signature" };

    static List<Dictionary<string, string>> ScanFixtures(out Repo.RunResult run)
    {
        string tmp = Repo.TempDir();
        string csv = Path.Combine(tmp, "scan.csv");
        var args = new List<string> { "--local-path", Repo.Fixtures, "-c", csv, "-q" };
        args.AddRange(MetadataOptions);
        run = Repo.Run(args.ToArray());
        Assert.True(run.ExitCode == 0, $"code {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
        return Csv.ReadRows(csv);
    }

    static string RelativeDir(string uncDirectory, string root)
    {
        string rel = uncDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? uncDirectory[root.Length..] : uncDirectory;
        return rel.Replace('\\', '/').Trim('/');
    }

    [Fact]
    public void Scan_local_produit_19_colonnes_et_les_7_fixtures()
    {
        var rows = ScanFixtures(out _);
        Assert.Equal(7, rows.Count);
        var byName = rows.ToDictionary(r => r["Name"]);
        Assert.Equal(new[] { "ancien rapport.doc", "config.ini", "logo.png", "notes réunion.txt", "rapport financier 2024.pdf", "tableau 2019.xls", "vide.txt" },
            byName.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("pdf", byName["rapport financier 2024.pdf"]["FileSignature"]);
        Assert.Equal("doc", byName["ancien rapport.doc"]["FileSignature"]);
        Assert.Equal("xls", byName["tableau 2019.xls"]["FileSignature"]);
        Assert.Equal("png", byName["logo.png"]["FileSignature"]);
        Assert.Equal("unknown", byName["vide.txt"]["FileSignature"]);
        Assert.Equal("0", byName["vide.txt"]["FileSize"]);
        Assert.Equal("33", byName["logo.png"]["FileSize"]);
        Assert.Equal("ini", byName["config.ini"]["Extension"]);
        Assert.Equal("sous dossier", Path.GetFileName(byName["config.ini"]["UNCDirectory"]));
        Assert.Equal("dossier été", Path.GetFileName(byName["notes réunion.txt"]["UNCDirectory"]));
        foreach (var r in rows)
        {
            Assert.Equal("localhost", r["Host"]);
            Assert.Equal(@"\\localhost\LOCAL_SCAN\", r["Base"]);
            Assert.Equal("LOCAL_FIXED", r["DirectoryType"]);
            Assert.Matches("^[0-9a-f]{16}$", r["FastHash"]);
            Assert.Matches(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}$", r["LastWriteTime"]);
            Assert.Matches(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}$", r["AccessTime"]);
            Assert.NotEqual("", r["Owner"]);
        }
    }

    /// <summary>
    /// Sans --sizefile, la colonne FileSize est VIDE, jamais « 0 ».
    ///
    /// Elle valait « 0 », indiscernable d'un fichier réellement vide : docia lisait
    /// un partage entier à 0 octet et l'excluait « fichier trop petit », sans un mot.
    /// Le champ vide déclenche au contraire son compteur `size_defaulted`.
    /// </summary>
    [Fact]
    public void Sans_sizefile_la_taille_est_vide_et_non_zero()
    {
        string tmp = Repo.TempDir();
        string sans = Path.Combine(tmp, "sans.csv");
        string avec = Path.Combine(tmp, "avec.csv");
        var r1 = Repo.Run("--local-path", Repo.Fixtures, "-c", sans, "-q");
        var r2 = Repo.Run("--local-path", Repo.Fixtures, "-c", avec, "-q", "--sizefile");
        Assert.True(r1.ExitCode == 0 && r2.ExitCode == 0, $"{r1.ExitCode}/{r2.ExitCode}\n{r1.Stderr}\n{r2.Stderr}");

        Assert.All(Csv.ReadRows(sans), r => Assert.Equal("", r["FileSize"]));

        // Avec l'option, « 0 » redevient un fait : le fichier est vraiment vide.
        var byName = Csv.ReadRows(avec).ToDictionary(r => r["Name"]);
        Assert.Equal("0", byName["vide.txt"]["FileSize"]);
        Assert.Equal("33", byName["logo.png"]["FileSize"]);
    }

    [Fact]
    public void Scan_local_correspond_au_CSV_d_or_sur_les_colonnes_stables()
    {
        var actual = ScanFixtures(out _);
        var golden = Csv.ReadRows(Repo.Golden);
        string goldenRoot = golden.Select(r => r["UNCDirectory"]).OrderBy(p => p.Length).First();
        string[] stable = { "Name", "Extension", "DirectoryType", "Base", "FileSize", "FastHash", "FileSignature" };
        string Key(Dictionary<string, string> r, string root) =>
            string.Join("|", stable.Select(c => r[c]).Append(RelativeDir(r["UNCDirectory"], root)));
        var expected = golden.Select(r => Key(r, goldenRoot)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var got = actual.Select(r => Key(r, Repo.Fixtures)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, got);
    }

    [Fact]
    public void Guillemets_espaces_et_accents_dans_les_noms_relus_intacts()
    {
        string tmp = Repo.TempDir();
        string sub = Path.Combine(tmp, "Dossier Été, archivé");
        Directory.CreateDirectory(sub);
        var names = new List<string> { "résumé de réunion (v2).txt", "budget, prévisionnel.csv" };
        if (!OperatingSystem.IsWindows())
            names.Add("rapport \"final\".txt");
        foreach (var n in names)
            File.WriteAllText(Path.Combine(sub, n), "contenu " + n);
        string csv = Path.Combine(tmp, "scan.csv");
        var run = Repo.Run("--local-path", sub, "-c", csv, "-q", "--sizefile");
        Assert.Equal(0, run.ExitCode);
        var rows = Csv.ReadRows(csv);
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), rows.Select(r => r["Name"]).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(rows, r => Assert.Equal(sub, r["UNCDirectory"]));
        if (!OperatingSystem.IsWindows())
        {
            string raw = File.ReadAllLines(csv).Single(l => l.Contains("final"));
            Assert.StartsWith("\"rapport \"\"final\"\".txt\",", raw);
        }
    }

    [Fact]
    public void Scan_local_sans_identifiants_fonctionne_sur_toutes_les_plateformes()
    {
        string csv = Path.Combine(Repo.TempDir(), "scan.csv");
        var run = Repo.Run("--local-path", Repo.Fixtures, "-c", csv, "-q");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(7, Csv.ReadRows(csv).Count);
    }
}
