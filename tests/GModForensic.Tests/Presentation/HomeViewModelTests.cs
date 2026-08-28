using GModForensic.Abstractions;
using GModForensic.Presentation;
using Xunit;

namespace GModForensic.Tests.Presentation;

public sealed class HomeViewModelTests
{
    [Fact]
    public void Le_scan_ne_peut_pas_demarrer_sans_consentement_ni_tracabilite()
    {
        var home = new HomeViewModel(new FakeScanSession());

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
        var home = new HomeViewModel(new FakeScanSession(limited));

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
        var home = new HomeViewModel(new FakeScanSession(limited))
        {
            OperatorName = "staff.durand",
            SubjectIdentifier = "joueur#4412",
            ConsentGiven = true,
            Profile = ScanProfile.Deep,
        };

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
        var home = new HomeViewModel(new FakeScanSession())
        {
            FileSystemRoots = "D:\\Jeux\\GarrysMod\r\n  E:\\Partage  \r\n",
        };

        var roots = home.BuildConfiguration().FileSystemRoots;

        Assert.Equal(["D:\\Jeux\\GarrysMod", "E:\\Partage"], roots);
    }
}
