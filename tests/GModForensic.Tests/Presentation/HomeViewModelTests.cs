using GModForensic.Abstractions;
using GModForensic.Presentation;
using Xunit;

namespace GModForensic.Tests.Presentation;

public sealed class HomeViewModelTests
{
    /// <summary>Construit l'ecran puis lui applique les capacites, comme le fait le shell.</summary>
    private static HomeViewModel CreateHome(FakeScanSession session)
    {
        var home = new HomeViewModel(session);
        home.ApplyCapabilities(session.MeasureCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult());
        return home;
    }

    [Fact]
    public void Tant_que_les_privileges_ne_sont_pas_mesures_l_ecran_le_dit_et_bloque_le_scan()
    {
        var session = new FakeScanSession();
        var home = new HomeViewModel(session)
        {
            OperatorName = "staff.durand",
            SubjectIdentifier = "joueur#4412",
            ConsentGiven = true,
        };

        // Construire l'ecran ne doit RIEN sonder : la mesure interroge le jeton, les volumes
        // et le dossier Prefetch, et doit avoir lieu apres l'affichage de la fenetre.
        Assert.Equal(0, session.MeasureCallCount);
        Assert.True(home.IsMeasuring);
        Assert.Empty(home.Modules);
        Assert.False(home.CanStart);
        Assert.Contains("cours", home.CapabilitySummary, StringComparison.OrdinalIgnoreCase);

        home.ApplyCapabilities(FakeScanSession.Everything);

        Assert.False(home.IsMeasuring);
        Assert.NotEmpty(home.Modules);
        Assert.True(home.CanStart);
    }

    [Fact]
    public void Le_scan_ne_peut_pas_demarrer_sans_consentement_ni_tracabilite()
    {
        var home = CreateHome(new FakeScanSession());

        Assert.False(home.CanStart);

        home.OperatorName = "staff.durand";
        Assert.False(home.CanStart);

        home.SubjectIdentifier = "joueur#4412";
        Assert.False(home.CanStart);

        // L'outil lit des artefacts detailles sur une machine personnelle : le consentement
        // et la tracabilite sont des prerequis, pas des options.
        home.ConsentGiven = true;
        Assert.True(home.CanStart);
        Assert.True(home.StartCommand.CanExecute(null));
    }

    [Fact]
    public void Les_modules_indisponibles_sont_annonces_avec_leur_motif_avant_le_scan()
    {
        var limited = FakeScanSession.Everything with { IsElevated = false };
        var home = CreateHome(new FakeScanSession(limited));

        var usn = home.Modules.Single(m => m.Id == "usn");

        Assert.False(usn.IsAvailable);
        Assert.Contains("administrateur", usn.UnavailableReason!, StringComparison.OrdinalIgnoreCase);

        // Ils sont aussi regroupes pour etre lisibles sans derouler la liste complete (§2).
        Assert.Contains(home.Unavailable, m => m.Id == "usn");
        Assert.True(home.HasUnavailableModules);
    }

    [Fact]
    public void Un_module_indisponible_est_desactive_dans_la_configuration_produite()
    {
        var limited = FakeScanSession.Everything with { IsElevated = false };
        var home = CreateHome(new FakeScanSession(limited));
        home.OperatorName = "staff.durand";
        home.SubjectIdentifier = "joueur#4412";
        home.ConsentGiven = true;
        home.Profile = ScanProfile.Deep;

        home.Modules.Single(m => m.Id == "prefetch").IsEnabled = false;

        var configuration = home.BuildConfiguration();

        Assert.Contains("usn", configuration.DisabledModuleIds);
        Assert.Contains("prefetch", configuration.DisabledModuleIds);
        Assert.DoesNotContain("registre", configuration.DisabledModuleIds);
        Assert.Equal(ScanProfile.Deep, configuration.Profile);
        Assert.True(configuration.ConsentGiven);
        Assert.Equal("staff.durand", configuration.OperatorName);
    }

    [Fact]
    public void Les_repertoires_saisis_remplacent_les_valeurs_par_defaut()
    {
        var home = CreateHome(new FakeScanSession());
        home.FileSystemRoots = "D:\\Jeux\\GarrysMod\r\n  E:\\Partage  \r\n";

        var roots = home.BuildConfiguration().FileSystemRoots;

        Assert.Equal(["D:\\Jeux\\GarrysMod", "E:\\Partage"], roots);
    }
}
