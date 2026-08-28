using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Engine;

namespace GModForensic.Presentation;

/// <summary>Ecran de scan : progression globale, etape courante, etat par module, annulation.</summary>
public sealed partial class ScanViewModel : ObservableObject
{
    private const int MaxLogLines = 300;

    private readonly Dictionary<string, ModuleRowViewModel> _rows =
        new(StringComparer.OrdinalIgnoreCase);

    private long _lastRenderedSequence;

    [ObservableProperty]
    private double _overallFraction;

    [ObservableProperty]
    private string _currentStep = "Preparation...";

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private int _itemsExamined;

    [ObservableProperty]
    private int _observationsCollected;

    [ObservableProperty]
    private bool _isRunning;

    public ObservableCollection<ModuleRowViewModel> Modules { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>Action d'annulation, fournie par le shell.</summary>
    public Action? CancelRequested { get; set; }

    public string OverallPercentText =>
        (OverallFraction * 100).ToString("0", CultureInfo.CurrentCulture) + " %";

    public string ElapsedText => Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Le compteur d'indicateurs affiche pendant le scan est PROVISOIRE : la correlation
    /// fusionne ensuite des observations en detections composites. Le libelle le dit.
    /// </summary>
    public string ObservationsText =>
        ObservationsCollected == 0
            ? "—"
            : $"{ObservationsCollected} (provisoire)";

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => CancelRequested?.Invoke();

    /// <summary>Prepare l'ecran pour un nouveau scan.</summary>
    public void Reset(IEnumerable<IScanModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _lastRenderedSequence = 0;
        OverallFraction = 0;
        CurrentStep = "Preparation...";
        Elapsed = TimeSpan.Zero;
        ItemsExamined = 0;
        ObservationsCollected = 0;

        Modules.Clear();
        LogLines.Clear();
        _rows.Clear();

        foreach (var module in modules)
        {
            var row = new ModuleRowViewModel(module.Id, module.DisplayName);
            _rows[module.Id] = row;
            Modules.Add(row);
        }

        IsRunning = true;
    }

    /// <summary>
    /// Applique une notification de progression.
    /// <para>
    /// Les notifications de plusieurs modules paralleles peuvent arriver dans le desordre :
    /// afficher une notification perimee ferait visiblement reculer la barre.
    /// </para>
    /// </summary>
    public void ApplyProgress(ScanProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Sequence <= _lastRenderedSequence)
        {
            return;
        }

        _lastRenderedSequence = snapshot.Sequence;

        OverallFraction = snapshot.OverallFraction;
        CurrentStep = snapshot.CurrentStep;
        Elapsed = snapshot.Elapsed;
        ItemsExamined = snapshot.ItemsExamined;
        ObservationsCollected = snapshot.ObservationsCollected;

        foreach (var module in snapshot.Modules)
        {
            if (_rows.TryGetValue(module.ModuleId, out var row))
            {
                row.Apply(module);
            }
        }
    }

    public void AppendLog(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        LogLines.Add(entry.ToString());

        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }
    }

    public void Finish(ScanRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        IsRunning = false;
        Elapsed = result.Elapsed;
        ItemsExamined = result.ItemsExamined;

        CurrentStep = result.WasCancelled
            ? $"Scan interrompu apres {result.Elapsed.TotalSeconds:0.0} s — resultats partiels conserves."
            : $"Scan termine en {result.Elapsed.TotalSeconds:0.0} s.";
    }

    partial void OnOverallFractionChanged(double value) => OnPropertyChanged(nameof(OverallPercentText));

    partial void OnElapsedChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedText));

    partial void OnObservationsCollectedChanged(int value) => OnPropertyChanged(nameof(ObservationsText));

    partial void OnIsRunningChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();
}
