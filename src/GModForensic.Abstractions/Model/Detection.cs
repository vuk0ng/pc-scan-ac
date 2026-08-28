namespace GModForensic.Abstractions.Model;

/// <summary>
/// Un JUGEMENT, produit exclusivement par le moteur de detection — jamais par un module.
/// </summary>
public sealed record Detection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public required string RuleId { get; init; }

    public required ScanCategory Category { get; init; }

    public required Severity Severity { get; init; }

    public required Confidence Confidence { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public string? Path { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public required string Source { get; init; }

    public required IReadOnlyList<Evidence> Evidence { get; init; }

    public required ScoreBreakdown Score { get; init; }

    /// <summary>Explication en francais, lisible par un membre du staff non technicien.</summary>
    public required string Explanation { get; init; }

    /// <summary>
    /// Causes legitimes connues pouvant expliquer cette detection.
    /// <para>
    /// Ce champ est <c>required</c> DELIBEREMENT : il est impossible de creer une detection
    /// sans documenter ce qui pourrait l'expliquer sans triche. C'est le §22 encode dans le
    /// type plutot que confie a la bonne volonte du developpeur.
    /// </para>
    /// </summary>
    public required string FalsePositiveNote { get; init; }

    /// <summary>Identifiants des observations ayant produit cette detection.</summary>
    public IReadOnlyList<string> RelatedObservationIds { get; init; } = [];

    /// <summary>
    /// Detections atomiques remplacees par cette detection composite, pour eviter
    /// le double comptage et garder un rapport lisible.
    /// </summary>
    public IReadOnlyList<string> SupersededDetectionIds { get; init; } = [];
}
