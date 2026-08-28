using GModForensic.Abstractions.Model;

namespace GModForensic.Abstractions;

/// <summary>
/// Resultat d'un module. Un module qui echoue produit un resultat qui DECRIT son echec :
/// l'absence d'information est elle-meme une information a afficher, jamais un blanc
/// silencieux (§25).
/// </summary>
public sealed record ModuleResult
{
    public required string ModuleId { get; init; }

    public required ModuleStatus Status { get; init; }

    public IReadOnlyList<Observation> Observations { get; init; } = [];

    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];

    /// <summary>Nombre d'elements examines, affiche dans la progression et le rapport.</summary>
    public int ItemsExamined { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Motif lisible d'un statut non nominal : « Volume D: non NTFS ».</summary>
    public string? StatusReason { get; init; }

    /// <summary>Symbole affiche dans l'interface et le rapport (§25).</summary>
    public string StatusSymbol => Status switch
    {
        ModuleStatus.Success => "✓",
        ModuleStatus.Partial => "⚠",
        ModuleStatus.Failed => "✕",
        ModuleStatus.Skipped => "○",
        ModuleStatus.Cancelled => "⊘",
        _ => "·",
    };

    public static ModuleResult Skipped(string moduleId, string reason) => new()
    {
        ModuleId = moduleId,
        Status = ModuleStatus.Skipped,
        StatusReason = reason,
    };

    public static ModuleResult Failed(string moduleId, string reason, TimeSpan elapsed = default) => new()
    {
        ModuleId = moduleId,
        Status = ModuleStatus.Failed,
        StatusReason = reason,
        Elapsed = elapsed,
    };
}
