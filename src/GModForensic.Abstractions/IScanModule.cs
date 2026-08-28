using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;

namespace GModForensic.Abstractions;

/// <summary>Avancement d'un module, remonte a l'interface pendant le scan.</summary>
public readonly record struct ModuleProgress(
    string ModuleId,
    double Fraction,
    string? CurrentStep,
    int ItemsExamined);

/// <summary>Tout ce qu'un module recoit. Un module ne construit jamais ses propres dependances.</summary>
public sealed record ScanContext
{
    public required string ScanId { get; init; }
    public required ScanConfiguration Configuration { get; init; }
    public required Capabilities Capabilities { get; init; }
    public required IScanLogger Logger { get; init; }
    public required IFileFactsCache FileFacts { get; init; }

    /// <summary>Horloge injectable, pour des tests deterministes.</summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>Progression du module courant. Fournie par l'orchestrateur, jamais nulle.</summary>
    public required IProgress<ModuleProgress> Progress { get; init; }
}

/// <summary>
/// Contrat d'un module de collecte.
/// <para>
/// Un module produit des <see cref="Observation"/> — des faits. Il ne calcule aucun score et
/// n'emet aucun jugement : c'est le moteur de detection, qui voit toutes les sources
/// simultanement, qui produit les <see cref="Detection"/>.
/// </para>
/// </summary>
public interface IScanModule
{
    /// <summary>Identifiant technique stable, utilise dans les journaux et la configuration.</summary>
    string Id { get; }

    /// <summary>Libelle affiche : « Analyse du journal USN ».</summary>
    string DisplayName { get; }

    ScanCategory Category { get; }

    RequiredCapabilities Requires { get; }

    /// <summary>
    /// Poids relatif pour la barre de progression globale, et ordre d'execution :
    /// les modules legers passent en premier pour donner un retour visuel immediat.
    /// </summary>
    int Weight { get; }

    Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken);
}
