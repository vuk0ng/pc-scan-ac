using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>
/// Agrege la progression des modules en une progression globale ponderee.
/// <para>
/// Le poids de chaque module (<see cref="IScanModule.Weight"/>) evite l'effet « barre qui saute » :
/// un module lourd qui avance de 10 % fait plus progresser la barre qu'un module leger termine.
/// </para>
/// </summary>
internal sealed class ProgressTracker
{
    private sealed class State
    {
        public required string ModuleId { get; init; }
        public required string DisplayName { get; init; }
        public required int Weight { get; init; }
        public ModuleStatus Status { get; set; } = ModuleStatus.NotStarted;
        public double Fraction { get; set; }
        public string? CurrentStep { get; set; }
        public int ItemsExamined { get; set; }
        public string? StatusReason { get; set; }
        public int Observations { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, State> _states;
    private readonly List<State> _ordered;
    private readonly double _totalWeight;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _throttle;
    private readonly long _startTimestamp;
    private readonly IProgress<ScanProgressSnapshot>? _sink;

    private long _lastEmitTimestamp;
    private long _sequence;
    private bool _everEmitted;
    private string _currentStep = "Preparation...";

    public ProgressTracker(
        IReadOnlyList<IScanModule> modules,
        TimeProvider clock,
        TimeSpan throttle,
        long startTimestamp,
        IProgress<ScanProgressSnapshot>? sink)
    {
        _ordered = modules
            .Select(m => new State { ModuleId = m.Id, DisplayName = m.DisplayName, Weight = Math.Max(1, m.Weight) })
            .ToList();

        _states = _ordered.ToDictionary(s => s.ModuleId, StringComparer.OrdinalIgnoreCase);
        _totalWeight = _ordered.Sum(s => (double)s.Weight);
        _clock = clock;
        _throttle = throttle;
        _startTimestamp = startTimestamp;
        _sink = sink;
        _lastEmitTimestamp = startTimestamp;
    }

    public void SetStatus(string moduleId, ModuleStatus status)
    {
        ScanProgressSnapshot? snapshot;

        lock (_gate)
        {
            if (!_states.TryGetValue(moduleId, out var state))
            {
                return;
            }

            state.Status = status;

            if (status == ModuleStatus.Running)
            {
                _currentStep = state.DisplayName;
            }

            snapshot = BuildSnapshot();
        }

        Emit(snapshot, force: true);
    }

    /// <summary>
    /// Dernier nombre d'elements signale par un module.
    /// <para>
    /// Un module interrompu leve avant d'avoir pu construire son resultat : sans cette
    /// reprise, le travail deja effectue disparaitrait du rapport alors qu'il a bien eu lieu.
    /// </para>
    /// </summary>
    public int LastItemsExamined(string moduleId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(moduleId, out var state) ? state.ItemsExamined : 0;
        }
    }

    public void Report(ModuleProgress progress)
    {
        ScanProgressSnapshot? snapshot;

        lock (_gate)
        {
            if (!_states.TryGetValue(progress.ModuleId, out var state))
            {
                return;
            }

            // L'avancement d'un module ne recule jamais : un module qui reevalue son total a
            // la baisse ne doit pas faire reculer la barre globale.
            state.Fraction = Math.Max(state.Fraction, Math.Clamp(progress.Fraction, 0d, 1d));
            state.ItemsExamined = Math.Max(state.ItemsExamined, progress.ItemsExamined);

            if (!string.IsNullOrWhiteSpace(progress.CurrentStep))
            {
                state.CurrentStep = progress.CurrentStep;
                _currentStep = progress.CurrentStep;
            }

            snapshot = BuildSnapshot();
        }

        Emit(snapshot, force: false);
    }

    public void Complete(ModuleResult result)
    {
        ScanProgressSnapshot? snapshot;

        lock (_gate)
        {
            if (!_states.TryGetValue(result.ModuleId, out var state))
            {
                return;
            }

            state.Status = result.Status;
            // Un module ignore ou en echec compte comme termine : la barre doit atteindre 100 %.
            state.Fraction = 1d;
            state.ItemsExamined = Math.Max(state.ItemsExamined, result.ItemsExamined);
            state.Observations = result.Observations.Count;
            state.StatusReason = result.StatusReason;
            state.CurrentStep = null;

            snapshot = BuildSnapshot();
        }

        Emit(snapshot, force: true);
    }

    /// <summary>Emission finale, jamais soumise a la cadence.</summary>
    public void Finish()
    {
        ScanProgressSnapshot snapshot;

        lock (_gate)
        {
            _currentStep = "Termine";
            snapshot = BuildSnapshot();
        }

        Emit(snapshot, force: true);
    }

    /// <summary>Construit un instantane. Appele sous verrou : c'est ce qui ordonne les sequences.</summary>
    private ScanProgressSnapshot BuildSnapshot() => new()
    {
        Sequence = ++_sequence,
        OverallFraction = _totalWeight <= 0
            ? 1d
            : Math.Clamp(_ordered.Sum(s => s.Weight * s.Fraction) / _totalWeight, 0d, 1d),
        CurrentStep = _currentStep,
        Elapsed = _clock.GetElapsedTime(_startTimestamp),
        ItemsExamined = _ordered.Sum(s => s.ItemsExamined),
        ObservationsCollected = _ordered.Sum(s => s.Observations),
        Modules = _ordered.Select(s => new ModuleSnapshot
        {
            ModuleId = s.ModuleId,
            DisplayName = s.DisplayName,
            Status = s.Status,
            Fraction = s.Fraction,
            CurrentStep = s.CurrentStep,
            ItemsExamined = s.ItemsExamined,
            StatusReason = s.StatusReason,
        }).ToArray(),
    };

    private void Emit(ScanProgressSnapshot? snapshot, bool force)
    {
        if (snapshot is null || _sink is null)
        {
            return;
        }

        if (!force && _everEmitted && _throttle > TimeSpan.Zero)
        {
            long last = Interlocked.Read(ref _lastEmitTimestamp);
            if (_clock.GetElapsedTime(last) < _throttle)
            {
                return;
            }
        }

        Interlocked.Exchange(ref _lastEmitTimestamp, _clock.GetTimestamp());
        _everEmitted = true;
        _sink.Report(snapshot);
    }
}
