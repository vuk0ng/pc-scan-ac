using GModForensic.Abstractions.Model;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Detection.Scoring;

/// <summary>Bande d'interpretation du score global. Aucune ne parle de triche (§21).</summary>
public sealed record ScoreBand(int Minimum, string Label, string Guidance);

/// <summary>Score global d'un scan, avec le detail permettant de l'expliquer.</summary>
public sealed record GlobalScore
{
    public required int Value { get; init; }
    public required ScoreBand Band { get; init; }
    public required IReadOnlyDictionary<ScanCategory, int> ByCategory { get; init; }
    public required int CriticalCount { get; init; }
    public required int HighCount { get; init; }
    public required int MediumCount { get; init; }
    public required int LowCount { get; init; }
}

/// <summary>
/// Agrege les detections en un score global.
/// <para>
/// Deux niveaux, deux formules, deliberement (§21) : a l'interieur d'une detection les
/// contributions s'ADDITIONNENT (<see cref="ScoreBreakdown"/>), tandis que le score global
/// combine les detections par PROBABILITE COMPLEMENTAIRE :
/// </para>
/// <code>S = 100 × ( 1 − ∏ ( 1 − pᵢ·cᵢ / 100 ) )</code>
/// <para>
/// Une simple somme donnerait « 340/100 » sur une machine ordinaire. Cette formule ne depasse
/// jamais 100 sans plafonnement artificiel, fait peser deux indicateurs moyens plus qu'un seul
/// mais moins que leur somme, et laisse un unique indicateur critique a 50 — il ne peut donc
/// jamais saturer le score a lui seul, ce qui est coherent avec « aucun element n'est une
/// preuve ».
/// </para>
/// </summary>
public static class ScoreAggregator
{
    /// <summary>Aucune categorie ne peut a elle seule depasser cette contribution.</summary>
    public const int CategoryCap = 60;

    private static readonly ScoreBand[] Bands =
    [
        new(80, "Indicateurs tres eleves", "Faisceau d'indicateurs concordants — verification manuelle prioritaire"),
        new(60, "Indicateurs eleves", "Verification manuelle necessaire"),
        new(40, "Indicateurs moderes", "Verification manuelle recommandee"),
        new(20, "Indicateurs faibles", "Elements a contextualiser"),
        new(0, "Aucun indicateur notable", "Aucun element marquant sur la fenetre couverte"),
    ];

    public static GlobalScore Compute(IEnumerable<DetectionRecord> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);

        var all = detections.ToArray();
        var byCategory = new Dictionary<ScanCategory, int>();

        foreach (var group in all.GroupBy(d => d.Category))
        {
            byCategory[group.Key] = Math.Min(CategoryCap, CombineWithinCategory(group));
        }

        var global = Combine(byCategory.Values.Select(v => v / 100d));

        return new GlobalScore
        {
            Value = (int)Math.Round(global * 100, MidpointRounding.AwayFromZero),
            Band = BandFor((int)Math.Round(global * 100, MidpointRounding.AwayFromZero)),
            ByCategory = byCategory,
            CriticalCount = all.Count(d => d.Severity == Severity.Critical),
            HighCount = all.Count(d => d.Severity == Severity.High),
            MediumCount = all.Count(d => d.Severity == Severity.Medium),
            LowCount = all.Count(d => d.Severity == Severity.Low),
        };
    }

    public static ScoreBand BandFor(int score) =>
        Bands.First(b => score >= b.Minimum);

    private static int CombineWithinCategory(IEnumerable<DetectionRecord> detections)
    {
        var weights = new List<double>();

        // Rendements decroissants : la k-ieme detection d'une meme regle est ponderee 1/k.
        // Sans cela, cent .exe supprimes dans Temp suffiraient a saturer le score.
        foreach (var sameRule in detections.GroupBy(d => d.RuleId, StringComparer.Ordinal))
        {
            var rank = 1;

            foreach (var detection in sameRule.OrderByDescending(d => d.Score.Total))
            {
                var points = detection.Score.Total * ScoreBreakdown.Weight(detection.Confidence) / rank;
                weights.Add(points / 100d);
                rank++;
            }
        }

        return (int)Math.Round(Combine(weights) * 100, MidpointRounding.AwayFromZero);
    }

    /// <summary>1 − ∏ (1 − wᵢ), borne a [0, 1].</summary>
    private static double Combine(IEnumerable<double> weights)
    {
        var remaining = 1d;

        foreach (var weight in weights)
        {
            remaining *= 1d - Math.Clamp(weight, 0d, 1d);
        }

        return Math.Clamp(1d - remaining, 0d, 1d);
    }
}
