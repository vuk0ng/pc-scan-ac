using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>Etat d'un module a un instant donne, tel qu'affiche dans la liste de l'ecran de scan.</summary>
public sealed record ModuleSnapshot
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required ModuleStatus Status { get; init; }
    public required double Fraction { get; init; }
    public string? CurrentStep { get; init; }
    public int ItemsExamined { get; init; }
    public string? StatusReason { get; init; }
}

/// <summary>Progression globale du scan (§3).</summary>
public sealed record ScanProgressSnapshot
{
    /// <summary>
    /// Numero d'ordre croissant, attribue au moment de la construction.
    /// <para>
    /// Les notifications transitent par <see cref="IProgress{T}"/> et peuvent donc etre
    /// DELIVREES dans le desordre quand plusieurs modules progressent en parallele. Un
    /// consommateur doit ignorer toute notification dont la sequence est inferieure a la
    /// derniere affichee, sans quoi la barre de progression recule visiblement.
    /// </para>
    /// </summary>
    public required long Sequence { get; init; }

    public required double OverallFraction { get; init; }

    /// <summary>Etape affichee sous la barre : « Analyse du journal USN... ».</summary>
    public required string CurrentStep { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required int ItemsExamined { get; init; }

    public required IReadOnlyList<ModuleSnapshot> Modules { get; init; }

    /// <summary>
    /// Compteur PROVISOIRE d'observations collectees. Il ne s'agit pas du nombre final
    /// d'indicateurs : la correlation fusionne des observations en detections composites,
    /// le chiffre definitif ne peut donc sortir qu'apres agregation.
    /// </summary>
    public required int ObservationsCollected { get; init; }
}
