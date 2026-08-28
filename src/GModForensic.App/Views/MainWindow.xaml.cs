using System.Windows;
using GModForensic.Abstractions;
using GModForensic.Engine;
using GModForensic.Native.Security;
using GModForensic.Scanners;

namespace GModForensic.App.Views;

public partial class MainWindow : Window
{
    private readonly Capabilities _capabilities;
    private CancellationTokenSource? _cancellation;
    private long _lastRenderedSequence;

    public MainWindow()
    {
        InitializeComponent();

        _capabilities = CapabilityProbe.Measure();
        CapabilitiesText.Text = DescribeCapabilities(_capabilities);
    }

    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ModuleList.Items.Clear();
        _lastRenderedSequence = 0;

        _cancellation = new CancellationTokenSource();

        var logger = new InMemoryScanLogger();
        var orchestrator = new ScanOrchestrator(ModuleCatalog.CreateDemoModules());

        var context = new ScanContext
        {
            ScanId = Guid.NewGuid().ToString("n"),
            Configuration = new ScanConfiguration { ConsentGiven = true },
            Capabilities = _capabilities,
            Logger = logger,
            FileFacts = new FileFactsCache(new NullFileFactsProvider()),
            Clock = TimeProvider.System,
            Progress = new Progress<ModuleProgress>(),
        };

        // Progress<T> marshale automatiquement sur le thread d'interface :
        // aucune operation bloquante ne touche le fil UI, qui reste reactif et annulable.
        var progress = new Progress<ScanProgressSnapshot>(Render);

        try
        {
            var result = await orchestrator
                .RunAsync(context, progress, _cancellation.Token)
                .ConfigureAwait(true);

            CurrentStepText.Text = result.WasCancelled
                ? $"Scan interrompu apres {result.Elapsed.TotalSeconds:0.0} s — resultats partiels conserves."
                : $"Scan termine en {result.Elapsed.TotalSeconds:0.0} s.";
        }
        finally
        {
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void Render(ScanProgressSnapshot snapshot)
    {
        // Les notifications de plusieurs modules paralleles peuvent arriver dans le desordre :
        // afficher une notification perimee ferait visiblement reculer la barre.
        if (snapshot.Sequence <= _lastRenderedSequence)
        {
            return;
        }

        _lastRenderedSequence = snapshot.Sequence;
        OverallProgress.Value = snapshot.OverallFraction;
        CurrentStepText.Text = snapshot.CurrentStep;
        CountersText.Text =
            $"{snapshot.OverallFraction:P0}   ·   elements : {snapshot.ItemsExamined}   ·   {snapshot.Elapsed:mm\\:ss}";

        ModuleList.Items.Clear();

        foreach (var module in snapshot.Modules)
        {
            var symbol = module.Status switch
            {
                Abstractions.Model.ModuleStatus.Success => "✓",
                Abstractions.Model.ModuleStatus.Partial => "⚠",
                Abstractions.Model.ModuleStatus.Failed => "✕",
                Abstractions.Model.ModuleStatus.Skipped => "○",
                Abstractions.Model.ModuleStatus.Cancelled => "⊘",
                Abstractions.Model.ModuleStatus.Running => "⟳",
                _ => "·",
            };

            var suffix = module.StatusReason is null ? string.Empty : $"  — {module.StatusReason}";
            ModuleList.Items.Add($"{symbol}  {module.DisplayName,-38}{module.Fraction,6:P0}{suffix}");
        }
    }

    private static string DescribeCapabilities(Capabilities capabilities)
    {
        var summary =
            $"Administrateur : {YesNo(capabilities.IsElevated)}   ·   "
            + $"SeDebugPrivilege : {YesNo(capabilities.HasDebugPrivilege)}   ·   "
            + $"SeSecurityPrivilege : {YesNo(capabilities.HasSecurityPrivilege)}";

        return capabilities.Notes.Count == 0
            ? summary
            : summary + Environment.NewLine + string.Join(Environment.NewLine, capabilities.Notes);
    }

    private static string YesNo(bool value) => value ? "OUI" : "NON";
}
