using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GModForensic.Abstractions;
using GModForensic.Detection.Scoring;
using GModForensic.Engine;
using GModForensic.Presentation.Services;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Presentation;

/// <summary>Ecran d'export : choix des formats et du dossier de sortie.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly IReportExporter _exporter;

    private ScanRunResult? _result;
    private IReadOnlyList<DetectionRecord> _detections = [];
    private GlobalScore _score = ScoreAggregator.Compute([]);
    private ScanConfiguration _configuration = new();

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _includeJson = true;

    [ObservableProperty]
    private bool _includeText = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _lastExportFailed;

    public ExportViewModel(IReportExporter exporter, TimeProvider? clock = null)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));

        var stamp = (clock ?? TimeProvider.System)
            .GetLocalNow()
            .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        _outputDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GModForensicScanner", "Reports", stamp);
    }

    public ObservableCollection<string> WrittenFiles { get; } = [];

    /// <summary>Retour a l'ecran de resultats, fourni par le shell.</summary>
    public Action? BackRequested { get; set; }

    /// <summary>Ouverture du selecteur de dossier, fournie par l'application Windows.</summary>
    public Func<string, string?>? BrowseRequested { get; set; }

    /// <summary>
    /// Le rapport HTML — celui que lira le staff — arrive a l'etape 8. L'indiquer plutot que
    /// de proposer une case a cocher inerte.
    /// </summary>
    public string HtmlNotice => "Rapport HTML autonome : disponible a l'etape 8.";

    public bool CanExport => _result is not null && (IncludeJson || IncludeText);

    public void Load(
        ScanRunResult result,
        IReadOnlyList<DetectionRecord> detections,
        GlobalScore score,
        ScanConfiguration configuration)
    {
        _result = result;
        _detections = detections;
        _score = score;
        _configuration = configuration;

        WrittenFiles.Clear();
        StatusMessage = null;
        LastExportFailed = false;

        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (_result is null)
        {
            return;
        }

        WrittenFiles.Clear();

        try
        {
            var written = _exporter.Export(new ExportRequest
            {
                OutputDirectory = OutputDirectory,
                IncludeJson = IncludeJson,
                IncludeText = IncludeText,
                Result = _result,
                Detections = _detections,
                Score = _score,
                Configuration = _configuration,
            });

            foreach (var path in written)
            {
                WrittenFiles.Add(path);
            }

            LastExportFailed = false;
            StatusMessage = $"{written.Count} fichier(s) ecrit(s) dans {OutputDirectory}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Un echec d'ecriture ne doit jamais faire disparaitre les resultats du scan :
            // il est signale, et l'export reste retentable ailleurs.
            LastExportFailed = true;
            StatusMessage = $"Ecriture impossible : {ex.Message}";
        }
    }

    [RelayCommand]
    private void Browse()
    {
        var chosen = BrowseRequested?.Invoke(OutputDirectory);

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            OutputDirectory = chosen;
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    partial void OnIncludeJsonChanged(bool value) => NotifyCanExport();

    partial void OnIncludeTextChanged(bool value) => NotifyCanExport();

    private void NotifyCanExport()
    {
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }
}
