using System.Buffers.Binary;
using System.Text;
using GModForensic.Parsers;
using Xunit;

namespace GModForensic.Tests.Parsers;

public sealed class NtPathResolverTests
{
    private static readonly Dictionary<string, string> DeviceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"\Device\HarddiskVolume2"] = "C:",
        [@"\Device\HarddiskVolume4"] = "D:",
    };

    [Theory]
    // BAM et les journaux noyau ecrivent les chemins sous forme de peripherique NT.
    [InlineData(@"\Device\HarddiskVolume2\Users\joueur\a.exe", @"C:\Users\joueur\a.exe")]
    [InlineData(@"\Device\HarddiskVolume4\jeux\gmod.exe", @"D:\jeux\gmod.exe")]
    // Prefixes d'espace de noms NT.
    [InlineData(@"\??\C:\Windows\explorer.exe", @"C:\Windows\explorer.exe")]
    [InlineData(@"\\?\C:\Windows\explorer.exe", @"C:\Windows\explorer.exe")]
    // Forme rencontree en memoire et dans les raccourcis.
    [InlineData("file:///C:/Users/joueur/Downloads/x.exe", @"C:\Users\joueur\Downloads\x.exe")]
    [InlineData(@"C:\deja\normalise.exe", @"C:\deja\normalise.exe")]
    public void Les_ecritures_d_un_meme_chemin_convergent(string input, string expected) =>
        Assert.Equal(expected, NtPathResolver.Normalize(input, DeviceMap));

    [Fact]
    public void Un_peripherique_inconnu_reste_tel_quel_plutot_que_d_etre_invente()
    {
        const string path = @"\Device\HarddiskVolume9\quelque\part.exe";

        // Inventer une lettre de volume attribuerait des traces au mauvais disque.
        Assert.Equal(path, NtPathResolver.Normalize(path, DeviceMap));
    }

    [Fact]
    public void Un_partage_UNC_n_est_pas_ampute_de_son_prefixe()
    {
        const string path = @"\\?\UNC\serveur\partage\x.exe";

        Assert.Equal(path, NtPathResolver.Normalize(path, DeviceMap));
    }

    [Theory]
    [InlineData(@"C:\Users\joueur\AppData\Local\Temp\x.exe", true)]
    [InlineData(@"C:\Users\joueur\Downloads\x.exe", true)]
    [InlineData(@"C:\Program Files\Steam\steam.exe", false)]
    [InlineData("", false)]
    public void Les_chemins_temporaires_et_de_telechargement_sont_reconnus(string path, bool expected) =>
        Assert.Equal(expected, NtPathResolver.IsUserVolatilePath(path));

    [Fact]
    public void Le_nom_de_fichier_est_extrait_quelle_que_soit_la_forme()
    {
        Assert.True(NtPathResolver.TryGetFileName(@"C:\a\b\loader.exe", out var name));
        Assert.Equal("loader.exe", name);

        Assert.True(NtPathResolver.TryGetFileName("loader.exe", out name));
        Assert.Equal("loader.exe", name);

        Assert.False(NtPathResolver.TryGetFileName("   ", out _));
    }
}

public sealed class RegistryValueDecoderTests
{
    private static readonly DateTimeOffset Reference =
        new(2026, 8, 27, 21, 14, 38, TimeSpan.Zero);

    [Fact]
    public void Le_timestamp_BAM_est_lu_dans_les_huit_premiers_octets()
    {
        var value = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(value, Reference.ToFileTime());

        var timestamp = RegistryValueDecoders.ReadBamTimestamp(value);

        Assert.NotNull(timestamp);
        Assert.Equal(Reference, timestamp!.Value);
    }

    [Fact]
    public void Une_valeur_BAM_tronquee_ne_produit_pas_de_fausse_date()
    {
        Assert.Null(RegistryValueDecoders.ReadBamTimestamp(new byte[4]));
        Assert.Null(RegistryValueDecoders.ReadBamTimestamp(new byte[24]));
    }

    [Fact]
    public void UserAssist_donne_le_nombre_d_executions_et_la_derniere()
    {
        var value = new byte[72];
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(4), 7);
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(60), Reference.ToFileTime());

        var parsed = RegistryValueDecoders.ReadUserAssistCount(value);

        Assert.NotNull(parsed);
        Assert.Equal(7, parsed!.Value.RunCount);
        Assert.Equal(Reference, parsed.Value.LastRun);
    }

    [Fact]
    public void Une_structure_UserAssist_trop_courte_est_refusee()
    {
        // Les anciennes versions de Windows utilisent une structure differente : mieux vaut
        // ne rien affirmer que decoder de travers.
        Assert.Null(RegistryValueDecoders.ReadUserAssistCount(new byte[16]));
    }

    [Theory]
    // Les noms de valeur UserAssist sont des chemins encodes en ROT13.
    [InlineData(@"P:\Hfref\wbhrhe\Qbjaybnqf\ybnqre.rkr", @"C:\Users\joueur\Downloads\loader.exe")]
    [InlineData("Zvpebfbsg.Jvaqbjf.Rkcybere", "Microsoft.Windows.Explorer")]
    // Chiffres et ponctuation restent intacts.
    [InlineData(@"P:\n-o_p.rkr", @"C:\a-b_c.exe")]
    public void Le_ROT13_de_UserAssist_ne_touche_que_les_lettres(string input, string expected) =>
        Assert.Equal(expected, RegistryValueDecoders.DecodeRot13(input));

    [Fact]
    public void Le_ROT13_est_sa_propre_reciproque()
    {
        const string original = @"C:\Users\joueur\Downloads\loader.exe";

        Assert.Equal(original,
            RegistryValueDecoders.DecodeRot13(RegistryValueDecoders.DecodeRot13(original)));
    }

    [Fact]
    public void L_historique_binaire_de_7_Zip_est_decoupe_sur_les_caracteres_nuls()
    {
        string[] expected = [@"C:\Users\joueur\Downloads\pack.7z", @"D:\archives\mods.zip"];

        var bytes = new List<byte>();

        foreach (var entry in expected)
        {
            bytes.AddRange(Encoding.Unicode.GetBytes(entry));
            bytes.AddRange([0, 0]);
        }

        Assert.Equal(expected, RegistryValueDecoders.ReadUtf16StringList(bytes.ToArray()));
    }

    [Fact]
    public void Une_valeur_binaire_vide_ne_produit_aucune_entree()
    {
        Assert.Empty(RegistryValueDecoders.ReadUtf16StringList([]));
        Assert.Empty(RegistryValueDecoders.ReadUtf16StringList([0, 0, 0, 0]));
    }

    [Fact]
    public void Un_FILETIME_nul_ou_aberrant_donne_une_date_inconnue()
    {
        Assert.Null(RegistryValueDecoders.FromFileTime(0));
        Assert.Null(RegistryValueDecoders.FromFileTime(-1));
        Assert.Null(RegistryValueDecoders.FromFileTime(long.MaxValue));
    }
}
