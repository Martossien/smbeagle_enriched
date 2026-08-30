using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using SMBeagle.Enums;
using SMBeagle.FileDiscovery.Output;
using SMBeagle.HostDiscovery;
using SMBeagle.Output;
using SMBeagle.ShareDiscovery;
using SmbDirectory = SMBeagle.FileDiscovery.Directory;
using SmbFile = SMBeagle.FileDiscovery.File;

namespace SMBeagle.Tests;

/// <summary>Puits Serilog minimal : applique le formateur sur un TextWriter.</summary>
sealed class WriterSink(ITextFormatter formatter, TextWriter writer) : ILogEventSink
{
    public void Emit(LogEvent logEvent) => formatter.Format(logEvent, writer);
}

/// <summary>Tests en mémoire du formateur CSV (même chemin Serilog que la production).</summary>
public class CsvContractTests
{
    static readonly DateTime Stamp = new(2024, 3, 5, 14, 7, 9);

    static SmbFile MakeFile(string name, string uncDirectory, string shareName = "Partage", string host = "srv-fichiers")
    {
        var share = new Share(new Host(host), shareName, ShareTypeEnum.DISK);
        var dir = new SmbDirectory(uncDirectory, share) { DirectoryType = DirectoryTypeEnum.SMB };
        string ext = Path.GetExtension(name);
        var file = new SmbFile(name, uncDirectory + "\\" + name, ext, Stamp, Stamp, dir,
            fileSize: 1234, accessTime: Stamp, fileAttributes: "Archive", owner: "DOM\\alice",
            fastHash: "0123456789abcdef", fileSignature: "pdf");
        file.SetPermissions(read: true, write: false, delete: false);
        return file;
    }

    static string[] Render(params SmbFile[] files)
    {
        var sw = new StringWriter();
        using var logger = new LoggerConfiguration().WriteTo.Sink(new WriterSink(new CSVFormatter(), sw)).CreateLogger();
        foreach (var f in files)
        {
            var payload = new FileOutput(f) { Hostname = "poste.WORKGROUP", Username = "DOM\\alice" };
            logger.Information("{hostname}:{username}:{@File}", "poste.WORKGROUP", "DOM\\alice", payload);
        }
        return sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
    }

    [Fact]
    public void EnTete_19_colonnes_dans_l_ordre_du_contrat()
    {
        var lines = Render(MakeFile("a.pdf", @"\\srv-fichiers\Partage\dossier"));
        Assert.Equal(Csv.Header, Csv.Split(lines[0]));
        Assert.Equal(string.Join(",", Csv.Header), lines[0]);
    }

    [Fact]
    public void Guillemets_selectifs_chaines_quotees_autres_types_nus()
    {
        var lines = Render(MakeFile("a.pdf", @"\\srv-fichiers\Partage\dossier"));
        var flags = Csv.QuotedFlags(lines[1]);
        Assert.Equal(19, flags.Count);
        foreach (int i in Csv.QuotedColumns) Assert.True(flags[i], $"colonne {i} ({Csv.Header[i]}) devrait être entre guillemets");
        foreach (int i in Csv.UnquotedColumns) Assert.False(flags[i], $"colonne {i} ({Csv.Header[i]}) ne devrait pas être entre guillemets");
        var fields = Csv.Split(lines[1]);
        Assert.Equal("True", fields[8]);
        Assert.Equal("False", fields[9]);
        Assert.Equal("1234", fields[13]);
        Assert.Equal("SMB", fields[11]);
        Assert.DoesNotContain("\"", fields[6]);
    }

    [Fact]
    public void Guillemet_interne_double_selon_RFC_4180()
    {
        var lines = Render(MakeFile("rapport \"final\".pdf", @"\\srv-fichiers\Partage\dossier"));
        Assert.StartsWith("\"rapport \"\"final\"\".pdf\",", lines[1]);
        Assert.DoesNotContain("\\\"final", lines[1]); // plus d'échappement Serilog \"
        Assert.Equal("rapport \"final\".pdf", Csv.Split(lines[1])[0]);
        Assert.Equal(19, Csv.Split(lines[1]).Count);
    }

    [Fact]
    public void Chemin_UNC_avec_espaces_et_accents_rendu_tel_quel()
    {
        const string unc = @"\\srv-fichiers\Partage Été\Comptabilité 2024, clôture";
        var lines = Render(MakeFile("bilan été.xlsx", unc, shareName: "Partage Été"));
        var fields = Csv.Split(lines[1]);
        Assert.Equal("bilan été.xlsx", fields[0]);
        Assert.Equal(unc, fields[5]);
        Assert.Equal(@"\\srv-fichiers\Partage Été\", fields[12]);
        Assert.Equal("xlsx", fields[2]);
        Assert.Equal(19, fields.Count);
    }

    [Fact]
    public void En_tete_ecrit_une_seule_fois_par_formateur()
    {
        var lines = Render(MakeFile("a.pdf", @"\\s\p\d"), MakeFile("b.pdf", @"\\s\p\d"));
        Assert.Equal(3, lines.Length);
        Assert.Single(lines, l => l == string.Join(",", Csv.Header));
        var again = Render(MakeFile("c.pdf", @"\\s\p\d"));
        Assert.Equal(2, again.Length);
    }

    [Fact]
    public void Dates_au_format_fixe_dd_MM_yyyy_HH_mm_ss_quelle_que_soit_la_culture()
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "de-DE", "" })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var fields = Csv.Split(Render(MakeFile("a.pdf", @"\\s\p\d"))[1]);
                Assert.Equal("05/03/2024 14:07:09", fields[6]);
                Assert.Equal("05/03/2024 14:07:09", fields[7]);
                Assert.Equal("05/03/2024 14:07:09", fields[14]);
                Assert.Equal("1234", fields[13]);
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }

    [Fact]
    public void Liste_des_colonnes_exposee_egale_a_l_en_tete_reel()
    {
        Assert.Equal(Csv.Header, FileOutput.Columns);
        Assert.Equal(Csv.Header, Csv.Split(Render(MakeFile("a.pdf", @"\\s\p\d"))[0]));
    }

    [Fact]
    public void Extension_en_minuscules_sans_point_et_nom_avec_casse_preservee()
    {
        var lines = Render(MakeFile("Rapport.PDF", @"\\s\p\d"));
        var fields = Csv.Split(lines[1]);
        Assert.Equal("Rapport.PDF", fields[0]);
        Assert.Equal("pdf", fields[2]);
    }
}
