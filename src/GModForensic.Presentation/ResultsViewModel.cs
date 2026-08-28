using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GModForensic.Abstractions.Model;
using GModForensic.Detection.Scoring;
using GModForensic.Engine;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Presentation;

/// <summary>Une entree du filtre de gravite.</summary>
public sealed record SeverityOption(string Label, Severity? Value);

/// <summary>Ecran de resultats : score, repartition, liste filtrable, detail et preuve brute.</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly List<DetectionViewModel> _all = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Severity? _severityFilter;

    [ObservableProperty]
    private string? _categoryFilter;

    [ObservableProperty]
    private DetectionViewModel? _selected;

    [ObservableProperty]
    private GlobalScore _score = ScoreAggregator.Compute([]);

    [ObservableProperty]
    private bool _wasCancelled;

    [ObservableProperty]
    private string _coverageWindow = "fenetre non determinee";

    public ObservableCollection<DetectionViewModel> Detections { get; } = [];

    public ObservableCollection<ModuleRowViewModel> ModuleStates { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>Options du filtre de gravite, avec un libelle lisible pour la liste deroulante.</summary>
    public IReadOnlyList<SeverityOption> SeverityOptions { get; } =
    [
        new("Toutes gravites", null),
        new("Critiques", Severity.Critical),
        new("Eleves", Severity.High),
        new("Moyens", Severity.Medium),
        new("Faibles", Severity.Low),
    ];

    /// <summary>Demande d'export, fournie par le shell.</summary>
    public Action? ExportRequested { get; set; }

    /// <summary>Demande de retour a l'accueil, fournie par le shell.</summary>
    public Action? NewScanRequested { get; set; }

    public int ScoreValue => Score.Value;

    public string BandLabel => Score.Band.Label;

    public string BandGuidance => Score.Band.Guidance;

    public string Tone => Score.Value switch
    {
        >= 80 => "crit",
        >= 60 => "high",
        >= 40 => "med",
        _ => "low",
    };

    /// <summary>
    /// Clause fixe, non masquable, reprise du rapport : le score qualifie le dossier
    /// d'indicateurs, jamais la personne.
    /// </summary>
    public string Disclaimer =>
        "Ce resultat recense des indicateurs. Aucun element ci-dessous ne constitue a lui seul "
        + "une preuve d'utilisation de cheat. Toute conclusion doit reposer sur une verification manuelle.";

    public string EmptyMessage =>
        _all.Count == 0
            ? "Aucun indicateur n'a ete retenu sur la fenetre couverte."
            : "Aucun indicateur ne correspond au filtre courant.";

    public bool HasDetections => Detections.Count > 0;

    public void Load(ScanRunResult result, IEnumerable<DetectionRecord> detections)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(detections);

        _all.Clear();
        _all.AddRange(detections.Select(d => new DetectionViewModel(d)));

        Score = ScoreAggregator.Compute(_all.Select(d => d.Detection));
        WasCancelled = result.WasCancelled;

        ModuleStates.Clear();

        foreach (var module in result.ModuleResults)
        {
            var row = new ModuleRowViewModel(module.ModuleId, module.ModuleId)
            {
                Status = module.Status,
                ItemsExamined = module.ItemsExamined,
                StatusReason = module.StatusReason,
                Fraction = 1,
            };

            ModuleStates.Add(row);
        }

        Categories.Clear();
        Categories.Add("Toutes");

        foreach (var category in _all.Select(d => d.CategoryLabel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Categories.Add(category);
        }

        CategoryFilter = "Toutes";
        SeverityFilter = null;
        SearchText = string.Empty;

        ApplyFilter();
        NotifyScore();
    }

    [RelayCommand]
    private void Export() => ExportRequested?.Invoke();

    [RelayCommand]
    private void NewScan() => NewScanRequested?.Invoke();

    [RelayCommand]
    private void ClearFilters()
    {
        SeverityFilter = null;
        CategoryFilter = "Toutes";
        SearchText = string.Empty;
    }

    public void ApplyFilter()
    {
        var query = _all.AsEnumerable();

        if (SeverityFilter is { } severity)
        {
            query = query.Where(d => d.Severity == severity);
        }

        if (CategoryFilter is not null && CategoryFilter != "Toutes")
        {
            query = query.Where(d => string.Equals(d.CategoryLabel, CategoryFilter, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(d =>
                d.SearchIndex.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        Detections.Clear();

        // Les plus graves d'abord, puis les scores les plus eleves : le staff lit de haut en bas.
        foreach (var detection in query.OrderByDescending(d => d.Severity).ThenByDescending(d => d.Score))
        {
            Detections.Add(detection);
        }

        Selected = Detections.FirstOrDefault();

        OnPropertyChanged(nameof(HasDetections));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSeverityFilterChanged(Severity? value) => ApplyFilter();

    partial void OnCategoryFilterChanged(string? value) => ApplyFilter();

    partial void OnScoreChanged(GlobalScore value) => NotifyScore();

    private void NotifyScore()
    {
        OnPropertyChanged(nameof(ScoreValue));
        OnPropertyChanged(nameof(BandLabel));
        OnPropertyChanged(nameof(BandGuidance));
        OnPropertyChanged(nameof(Tone));
    }
}
