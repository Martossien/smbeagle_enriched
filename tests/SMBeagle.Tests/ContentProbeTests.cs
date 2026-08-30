using SMBeagle.FileDiscovery;

namespace SMBeagle.Tests;

/// <summary>L'implémentation unique du hash rapide et de la signature, quel que soit le lecteur.</summary>
public class ContentProbeTests
{
    /// <summary>Flux qui ne rend qu'un octet par lecture, comme un serveur SMB qui fragmente.</summary>
    sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    }

    static string Fixture(string relative) => Path.Combine(Repo.Fixtures, relative);

    [Fact]
    public void Hash_et_signature_des_fixtures_identiques_au_CSV_d_or()
    {
        var golden = Csv.ReadRows(Repo.Golden).ToDictionary(r => r["Name"]);
        foreach (var (name, relative) in new[] { ("rapport financier 2024.pdf", "rapport financier 2024.pdf"), ("logo.png", "logo.png"), ("vide.txt", "vide.txt"), ("config.ini", Path.Combine("dossier été", "sous dossier", "config.ini")) })
        {
            var probe = ContentProbe.ProbeLocal(Fixture(relative), wantHash: true, wantSignature: true);
            Assert.Equal(golden[name]["FastHash"], probe.FastHash);
            Assert.Equal(golden[name]["FileSignature"], probe.FileSignature);
        }
    }

    [Fact]
    public void Fichiers_Office_OLE2_ont_un_hash_et_une_signature_doc_xls()
    {
        // Régression : FileSignatures lève CFCorruptedFileException sur 32 octets d'un
        // fichier OLE2 ; l'exception vidait aussi le hash.
        var doc = ContentProbe.ProbeLocal(Fixture(Path.Combine("dossier été", "ancien rapport.doc")), true, true);
        Assert.Matches("^[0-9a-f]{16}$", doc.FastHash);
        Assert.Equal("doc", doc.FileSignature);
        var xls = ContentProbe.ProbeLocal(Fixture("tableau 2019.xls"), true, true);
        Assert.Matches("^[0-9a-f]{16}$", xls.FastHash);
        Assert.Equal("xls", xls.FileSignature);
    }

    [Fact]
    public void OLE2_dont_la_structure_depasse_l_en_tete_donne_ole_et_le_hash_est_conserve()
    {
        // Un .ppt de 650 Ko a son répertoire au-delà des 64 Ko lus : on simule avec un
        // .doc tronqué à 1 Ko (en-tête OLE2 valide, structure incomplète).
        byte[] tronque = System.IO.File.ReadAllBytes(Fixture("tableau 2019.xls"))[..1024];
        Assert.Equal("ole", ContentProbe.Signature(tronque));
        var probe = ContentProbe.Probe(n => tronque, true, true);
        Assert.Matches("^[0-9a-f]{16}$", probe.FastHash);
        Assert.Equal("ole", probe.FileSignature);
    }

    [Fact]
    public void Signature_ne_leve_jamais_meme_sur_un_en_tete_tronque_ou_vide()
    {
        Assert.Equal("unknown", ContentProbe.Signature(Array.Empty<byte>()));
        Assert.Equal("unknown", ContentProbe.Signature(new byte[] { 0xD0, 0xCF }));
        byte[] zipTronque = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };
        Assert.Equal("zip", ContentProbe.Signature(zipTronque));
    }

    [Fact]
    public void Fichier_vide_a_le_hash_xxhash64_de_zero_octet_et_signature_unknown()
    {
        var probe = ContentProbe.ProbeLocal(Fixture("vide.txt"), true, true);
        Assert.Equal("ef46db3751d8e999", probe.FastHash);
        Assert.Equal("unknown", probe.FileSignature);
    }

    [Fact]
    public void Lecteur_fragmente_donne_le_meme_hash_que_le_fichier_local()
    {
        byte[] data = System.IO.File.ReadAllBytes(Fixture("rapport financier 2024.pdf"));
        var local = ContentProbe.ProbeLocal(Fixture("rapport financier 2024.pdf"), true, true);
        var fragmented = ContentProbe.Probe(n => ContentProbe.ReadHead(new TrickleStream(data), n), true, true);
        Assert.Equal(local, fragmented);
    }

    [Fact]
    public void Seuls_les_64_premiers_kilooctets_comptent()
    {
        var rnd = new Random(42);
        byte[] big = new byte[ContentProbe.FAST_HASH_BYTES + 1000];
        rnd.NextBytes(big);
        byte[] altered = (byte[])big.Clone();
        altered[^1] ^= 0xFF;
        var a = ContentProbe.Probe(n => ContentProbe.ReadHead(new MemoryStream(big), n), true, false);
        var b = ContentProbe.Probe(n => ContentProbe.ReadHead(new MemoryStream(altered), n), true, false);
        Assert.Equal(a.FastHash, b.FastHash);
        altered[0] ^= 0xFF;
        var c = ContentProbe.Probe(n => ContentProbe.ReadHead(new MemoryStream(altered), n), true, false);
        Assert.NotEqual(a.FastHash, c.FastHash);
    }

    [Fact]
    public void Options_desactivees_ne_lisent_rien_et_rendent_vide()
    {
        bool read = false;
        var probe = ContentProbe.Probe(n => { read = true; return Array.Empty<byte>(); }, false, false);
        Assert.False(read);
        Assert.Equal(ContentProbe.Result.Empty, probe);
        Assert.Equal(ContentProbe.Result.Empty, ContentProbe.ProbeLocal(Path.Combine(Repo.Fixtures, "inexistant.bin"), true, true));
    }
}
