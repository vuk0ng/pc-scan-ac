using GModForensic.Abstractions.Model;
// Le namespace GModForensic.Detection masque le type Detection du modele :
// l'alias leve l'ambiguite sans renommer ni l'un ni l'autre.
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Detection;

/// <summary>
/// Transforme des observations en detections.
/// <para>
/// Squelette de l'etape 3 : la normalisation, la correlation et les regles arrivent a l'etape 6,
/// le scoring a l'etape 7. Le contrat est fige des maintenant pour que l'orchestrateur et
/// l'interface puissent etre construits et testes autour.
/// </para>
/// </summary>
public sealed class DetectionEngine
{
    private readonly IReadOnlyList<IDetectionRule> _rules;

    public DetectionEngine(IEnumerable<IDetectionRule>? rules = null) =>
        _rules = rules?.ToArray() ?? [];

    public int RuleCount => _rules.Count;

    public IReadOnlyList<DetectionRecord> Analyze(IEnumerable<Observation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (_rules.Count == 0)
        {
            return [];
        }

        // Etape 6 : normalisation puis regroupement reel par entite.
        // Provisoirement, chaque observation forme sa propre entite.
        var detections = new List<DetectionRecord>();

        foreach (var observation in observations)
        {
            var entity = new CorrelatedEntity
            {
                Id = observation.Id,
                File = observation.Subject,
                Observations = [observation],
            };

            foreach (var rule in _rules)
            {
                detections.AddRange(rule.Evaluate(entity));
            }
        }

        return detections;
    }
}
