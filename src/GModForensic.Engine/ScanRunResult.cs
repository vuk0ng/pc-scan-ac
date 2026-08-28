using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>
/// Sortie brute d'un scan : les faits collectes, l'etat de chaque module et les journaux.
/// Les detections sont produites ensuite, par le moteur de detection.
/// </summary>
public sealed record ScanRunResult
{
    public required string ScanId { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Un scan annule conserve ses resultats partiels et reste exportable.</summary>
    public required bool WasCancelled { get; init; }

    public required IReadOnlyList<ModuleResult> ModuleResults { get; init; }
    public required IReadOnlyList<LogEntry> Log { get; init; }
    public required IReadOnlyList<AccessEntry> AccessLog { get; init; }

    public IEnumerable<Observation> Observations =>
        ModuleResults.SelectMany(r => r.Observations);

    public int ItemsExamined => ModuleResults.Sum(r => r.ItemsExamined);

    public int Count(ModuleStatus status) =>
        ModuleResults.Count(r => r.Status == status);
}
