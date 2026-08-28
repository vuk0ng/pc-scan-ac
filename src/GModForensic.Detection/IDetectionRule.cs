using GModForensic.Abstractions.Model;
// Le namespace GModForensic.Detection masque le type Detection du modele :
// l'alias leve l'ambiguite sans renommer ni l'un ni l'autre.
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Detection;

/// <summary>
/// Entite reconstituee a partir de plusieurs sources : c'est sur elle que raisonnent les regles.
/// Etoffee a l'etape 6 (correlateur).
/// </summary>
public sealed record CorrelatedEntity
{
    public required string Id { get; init; }
    public FileKey? File { get; init; }
    public required IReadOnlyList<Observation> Observations { get; init; }

    /// <summary>Nombre de modules distincts ayant contribue. Principal multiplicateur de confiance.</summary>
    public int IndependentSourceCount =>
        Observations.Select(o => o.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public bool Has(ObservationKind kind) => Observations.Any(o => o.Kind == kind);
}

public interface IDetectionRule
{
    string Id { get; }

    /// <summary>
    /// Causes legitimes connues, reprises dans <c>Detection.FalsePositiveNote</c>.
    /// Une regle incapable de dire comment elle peut se tromper n'entre pas dans le produit (§22).
    /// </summary>
    string FalsePositiveNote { get; }

    IEnumerable<DetectionRecord> Evaluate(CorrelatedEntity entity);
}
