using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using Xunit;

namespace GModForensic.Tests.Model;

public sealed class ContractTests
{
    [Fact]
    public void Deux_fichiers_homonymes_ne_sont_pas_la_meme_entite()
    {
        var downloads = new FileKey { FileName = "loader.exe", FullPath = @"c:\users\joueur\downloads\loader.exe" };
        var temp = new FileKey { FileName = "loader.exe", FullPath = @"c:\users\joueur\appdata\local\temp\loader.exe" };

        // Le nom seul ne fusionne jamais deux entites : c'est la garantie qui empeche
        // le moteur d'attribuer a un fichier les traces d'un autre.
        Assert.False(downloads.IsSameEntityAs(temp));
    }

    [Fact]
    public void Un_meme_hash_fusionne_deux_observations_de_sources_differentes()
    {
        const string hash = "9f2c1a4b8e0d7c6a5b3f2e1d0c9b8a7f6e5d4c3b2a1908f7e6d5c4b3a2918070";

        var fromUsn = new FileKey { FileName = "x.exe", Sha256 = hash, MftReference = 42, Volume = "C:" };
        var fromDisk = new FileKey { FileName = "autre-nom.exe", Sha256 = hash, FullPath = @"c:\temp\x.exe" };

        Assert.True(fromUsn.IsSameEntityAs(fromDisk));
    }

    [Fact]
    public void Le_score_d_une_detection_reproduit_l_exemple_du_cahier_des_charges()
    {
        // §21 : +15 supprime, +15 Prefetch, +20 ancien nom, +10 Downloads = 60/100.
        var score = new ScoreBuilder()
            .Add("USN_DELETED", "EXE recemment supprime", 15)
            .Add("PREFETCH_PRESENT", "EXE trouve dans le Prefetch", 15)
            .Add("RENAMED_SUSPICIOUS", "Ancien nom potentiellement suspect", 20)
            .Add("USER_DOWNLOAD_PATH", "Fichier provenant du dossier Downloads", 10)
            .Build();

        Assert.Equal(60, score.Total);
        Assert.Equal(4, score.Contributions.Count);
    }

    [Fact]
    public void Un_score_ne_depasse_jamais_cent_ni_ne_descend_sous_zero()
    {
        var saturated = new ScoreBuilder()
            .Add("A", "a", 80)
            .Add("B", "b", 80)
            .Build();

        Assert.Equal(100, saturated.Total);

        var negative = new ScoreBuilder().Add("C", "attenuation", -40).Build();
        Assert.Equal(0, negative.Total);
    }

    [Theory]
    [InlineData(Severity.Low, 5)]
    [InlineData(Severity.Medium, 15)]
    [InlineData(Severity.High, 30)]
    [InlineData(Severity.Critical, 50)]
    public void Le_bareme_de_base_est_celui_du_cahier_des_charges(Severity severity, int expected) =>
        Assert.Equal(expected, ScoreBreakdown.BasePoints(severity));

    [Fact]
    public void Une_preuve_trop_longue_est_tronquee_mais_sa_longueur_reelle_est_conservee()
    {
        var huge = new string('x', Evidence.MaxRawTextLength * 3);

        var evidence = Evidence.FromText("MemoryString", "pid 1234 @ 0x7ff0000", huge);

        Assert.Equal(Evidence.MaxRawTextLength, evidence.RawText!.Length);
        // La troncature doit rester visible : sinon le staff croit disposer de la valeur complete.
        Assert.Equal(huge.Length, evidence.OriginalLength);
    }

    [Fact]
    public void Les_capacites_manquantes_sont_expliquees_en_francais_lisible()
    {
        var none = Capabilities.None;

        var reason = none.ExplainMissing(
            RequiredCapabilities.Administrator | RequiredCapabilities.NtfsVolume);

        Assert.NotNull(reason);
        Assert.Contains("administrateur", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NTFS", reason, StringComparison.OrdinalIgnoreCase);

        Assert.Null(none.ExplainMissing(RequiredCapabilities.None));
    }
}
