using System.Globalization;
using GModForensic.Abstractions.Model;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Presentation;

/// <summary>Une detection telle qu'affichee et filtree dans l'ecran de resultats.</summary>
public sealed class DetectionViewModel
{
    public DetectionViewModel(DetectionRecord detection)
    {
        Detection = detection ?? throw new ArgumentNullException(nameof(detection));
    }

    public DetectionRecord Detection { get; }

    public string Name => Detection.Name;

    public string RuleId => Detection.RuleId;

    public Severity Severity => Detection.Severity;

    public ScanCategory Category => Detection.Category;

    public string CategoryLabel => Detection.Category.ToString();

    public string SeverityLabel => Detection.Severity switch
    {
        Severity.Critical => "CRITIQUE",
        Severity.High => "ELEVE",
        Severity.Medium => "MOYEN",
        Severity.Low => "FAIBLE",
        _ => "INFO",
    };

    /// <summary>Cle semantique de couleur, doublee par le libelle : la couleur n'est jamais seule.</summary>
    public string Tone => Detection.Severity switch
    {
        Severity.Critical => "crit",
        Severity.High => "high",
        Severity.Medium => "med",
        _ => "low",
    };

    public string ConfidenceLabel => Detection.Confidence switch
    {
        Confidence.High => "Confiance elevee",
        Confidence.Medium => "Confiance moyenne",
        _ => "Confiance faible",
    };

    public int Score => Detection.Score.Total;

    public string ScoreText => $"{Detection.Score.Total}/100";

    public string? Path => Detection.Path;

    public string Source => Detection.Source;

    public string Explanation => Detection.Explanation;

    public string FalsePositiveNote => Detection.FalsePositiveNote;

    public string TimestampText => Detection.Timestamp is { } timestamp
        ? timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        : "date inconnue";

    /// <summary>Detail ligne a ligne du score : c'est la reponse a « pourquoi ce score ? » (§21).</summary>
    public IReadOnlyList<string> ScoreLines =>
        Detection.Score.Contributions
            .Select(c => $"{c.Points,+4}  {c.Label}")
            .ToArray();

    public IReadOnlyList<Evidence> Evidence => Detection.Evidence;

    /// <summary>Texte sur lequel porte la recherche plein texte de l'ecran de resultats.</summary>
    public string SearchIndex =>
        string.Join(' ', Name, Detection.Description, Path, Source, RuleId, CategoryLabel);
}
