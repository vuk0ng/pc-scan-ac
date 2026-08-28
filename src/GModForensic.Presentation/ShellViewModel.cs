using CommunityToolkit.Mvvm.ComponentModel;
using GModForensic.Abstractions;
using GModForensic.Detection;
using GModForensic.Engine;
using GModForensic.Presentation.Services;

namespace GModForensic.Presentation;

public enum ShellScreen
{
    Home,
    Scan,
    Results,
    Export,
}

/// <summary>
/// Navigation entre les quatre ecrans et cycle de vie d'un scan.
/// <para>
/// Tout le pilotage est ici, sans une seule reference a WPF : ce fichier est integralement
/// testable, y compris l'annulation et l'enchainement des ecrans.
/// </para>
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IScanSession _session;
    private readonly DetectionEngine _engine;

    private CancellationTokenSource? _cancellation;
    private ScanConfiguration _configuration = new();

    [ObservableProperty]
    private ShellScreen _screen = ShellScreen.Home;

    [ObservableProperty]
    private ObservableObject _current;

    public ShellViewModel(
        IScanSession session,
        IReportExporter exporter,
        DetectionEngine? engine = null,
        TimeProvider? clock = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _engine = engine ?? new DetectionEngine();

        Home = new HomeViewModel(session) { StartRequested = StartScanAsync };
        Scan = new ScanViewModel { CancelRequested = CancelScan };
        Results = new ResultsViewModel
        {
            ExportRequested = () => Navigate(ShellScreen.Export),
            NewScanRequested = () => Navigate(ShellScreen.Home),
        };
        Export = new ExportViewModel(exporter, clock) { BackRequested = () => Navigate(ShellScreen.Results) };

        _current = Home;
    }

    /// <summary>Libelle de l'ecran courant, en francais — l'enumeration est technique.</summary>
    public string ScreenLabel => Screen switch
    {
        ShellScreen.Scan => "Scan en cours",
        ShellScreen.Results => "Resultats",
        ShellScreen.Export => "Export",
        _ => "Preparation",
    };

    public HomeViewModel Home { get; }

    public ScanViewModel Scan { get; }

    public ResultsViewModel Results { get; }

    public ExportViewModel Export { get; }

    /// <summary>Dernier resultat brut, conserve pour l'export meme apres une annulation.</summary>
    public ScanRunResult? LastResult { get; private set; }

    /// <summary>
    /// Mesure les privileges et renseigne l'ecran d'accueil.
    /// <para>
    /// A appeler APRES l'affichage de la fenetre : la mesure sonde le jeton, les volumes et le
    /// dossier Prefetch, et peut prendre plusieurs secondes. La lancer avant l'affichage
    /// laisserait un processus sans interface visible.
    /// </para>
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = await _session
            .MeasureCapabilitiesAsync(cancellationToken)
            .ConfigureAwait(true);

        Home.ApplyCapabilities(capabilities);
    }

    public async Task StartScanAsync()
    {
        _configuration = Home.BuildConfiguration();

        Scan.Reset(_session.Modules.Where(m => !_configuration.DisabledModuleIds.Contains(m.Id)));
        Navigate(ShellScreen.Scan);

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<ScanProgressSnapshot>(Scan.ApplyProgress);

        try
        {
            var result = await _session
                .RunAsync(_configuration, progress, _cancellation.Token)
                .ConfigureAwait(true);

            LastResult = result;
            Scan.Finish(result);

            foreach (var entry in result.Log)
            {
                Scan.AppendLog(entry);
            }

            var detections = _engine.Analyze(result.Observations);

            Results.Load(result, detections);
            Export.Load(result, detections, Results.Score, _configuration);

            // Un scan annule reste exploitable : on presente quand meme les resultats partiels.
            Navigate(ShellScreen.Results);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    public void CancelScan() => _cancellation?.Cancel();

    public void Navigate(ShellScreen screen)
    {
        Screen = screen;
        OnPropertyChanged(nameof(ScreenLabel));

        Current = screen switch
        {
            ShellScreen.Scan => Scan,
            ShellScreen.Results => Results,
            ShellScreen.Export => Export,
            _ => Home,
        };
    }
}
