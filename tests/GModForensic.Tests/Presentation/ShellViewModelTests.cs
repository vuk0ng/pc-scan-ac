using GModForensic.Detection;
using GModForensic.Presentation;
using GModForensic.Presentation.Demo;
using Xunit;

namespace GModForensic.Tests.Presentation;

public sealed class ShellViewModelTests
{
    private static ShellViewModel CreateShell(
        FakeScanSession? session = null,
        RecordingExporter? exporter = null)
    {
        var shell = new ShellViewModel(
            session ?? new FakeScanSession(),
            exporter ?? new RecordingExporter(),
            new DetectionEngine([new DemoDetectionRule()]));

        shell.Home.OperatorName = "staff.durand";
        shell.Home.SubjectIdentifier = "joueur#4412";
        shell.Home.ConsentGiven = true;

        return shell;
    }

    [Fact]
    public async Task Un_scan_enchaine_accueil_puis_scan_puis_resultats()
    {
        var shell = CreateShell();

        Assert.Equal(ShellScreen.Home, shell.Screen);

        await shell.StartScanAsync();

        Assert.Equal(ShellScreen.Results, shell.Screen);
        Assert.Same(shell.Results, shell.Current);
        Assert.NotNull(shell.LastResult);
        Assert.False(shell.LastResult!.WasCancelled);
        Assert.True(shell.Results.HasDetections);
    }

    [Fact]
    public async Task Un_scan_annule_presente_quand_meme_ses_resultats_partiels()
    {
        var session = new FakeScanSession();
        var shell = CreateShell(session);

        // Annulation des la premiere notification de progression, comme un clic tres precoce.
        shell.Scan.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanViewModel.OverallFraction))
            {
                shell.CancelScan();
            }
        };

        await shell.StartScanAsync();

        Assert.Equal(ShellScreen.Results, shell.Screen);
        Assert.True(shell.Results.WasCancelled);
        Assert.NotNull(shell.LastResult);
    }

    [Fact]
    public async Task La_navigation_resultats_export_retour_fonctionne()
    {
        var shell = CreateShell();
        await shell.StartScanAsync();

        shell.Results.ExportCommand.Execute(null);
        Assert.Equal(ShellScreen.Export, shell.Screen);
        Assert.Same(shell.Export, shell.Current);

        shell.Export.BackCommand.Execute(null);
        Assert.Equal(ShellScreen.Results, shell.Screen);

        shell.Results.NewScanCommand.Execute(null);
        Assert.Equal(ShellScreen.Home, shell.Screen);
    }

    [Fact]
    public async Task Les_modules_desactives_ne_sont_pas_affiches_pendant_le_scan()
    {
        var session = new FakeScanSession();
        var shell = CreateShell(session);

        shell.Home.Modules.Single(m => m.Id == "prefetch").IsEnabled = false;

        await shell.StartScanAsync();

        Assert.DoesNotContain(shell.Scan.Modules, m => m.ModuleId == "prefetch");
        Assert.Contains(shell.Scan.Modules, m => m.ModuleId == "registre");
        Assert.Contains("prefetch", session.LastConfiguration!.DisabledModuleIds);
    }
}
