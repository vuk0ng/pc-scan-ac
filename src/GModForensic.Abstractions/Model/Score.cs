namespace GModForensic.Abstractions.Model;

/// <summary>Une ligne du calcul de score, pour repondre a l'exigence « expliquer POURQUOI » (§21).</summary>
public sealed record ScoreContribution
{
    public required string RuleId { get; init; }

    /// <summary>Libelle affiche tel quel dans le rapport : « EXE recemment supprime ».</summary>
    public required string Label { get; init; }

    public required int Points { get; init; }
}

/// <summary>
/// Detail du score d'UNE detection. A ce niveau les contributions s'additionnent, exactement
/// comme l'exemple du §21 ; c'est l'agregation GLOBALE qui est saturante (voir ScoreAggregator).
/// </summary>
public sealed record ScoreBreakdown
{
    public IReadOnlyList<ScoreContribution> Contributions { get; init; } = [];

    /// <summary>Somme des contributions, bornee a [0, 100].</summary>
    public int Total => Math.Clamp(Contributions.Sum(c => c.Points), 0, 100);

    /// <summary>Bareme de base du §21.</summary>
    public static int BasePoints(Severity severity) => severity switch
    {
        Severity.Low => 5,
        Severity.Medium => 15,
        Severity.High => 30,
        Severity.Critical => 50,
        _ => 0,
    };

    /// <summary>Coefficient de confiance applique lors de l'agregation globale.</summary>
    public static double Weight(Confidence confidence) => confidence switch
    {
        Confidence.Low => 0.4,
        Confidence.Medium => 0.7,
        Confidence.High => 1.0,
        _ => 0.0,
    };
}

/// <summary>Constructeur mutable de <see cref="ScoreBreakdown"/>, utilise par les regles composites.</summary>
public sealed class ScoreBuilder
{
    private readonly List<ScoreContribution> _contributions = [];

    public ScoreBuilder Add(string ruleId, string label, int points)
    {
        _contributions.Add(new ScoreContribution { RuleId = ruleId, Label = label, Points = points });
        return this;
    }

    public ScoreBreakdown Build() => new() { Contributions = _contributions.AsReadOnly() };
}
