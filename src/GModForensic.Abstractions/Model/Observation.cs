namespace GModForensic.Abstractions.Model;

/// <summary>
/// Un FAIT collecte par un module. Jamais un jugement, jamais un score.
/// <para>
/// C'est la separation structurante de l'architecture : un module dit « le fichier X a ete
/// renomme le T, source USN ». Il ne dit jamais « suspect ». Seul le moteur de detection,
/// qui voit toutes les sources en meme temps, produit une <see cref="Detection"/>.
/// Sans cette separation, le scoring contextuel multi-modules (§21) serait impossible.
/// </para>
/// </summary>
public sealed record Observation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public required string ModuleId { get; init; }

    public required ObservationKind Kind { get; init; }

    /// <summary>Horodatage UTC du fait observe. <c>null</c> quand la source n'en fournit pas.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Entite fichier concernee, si le fait porte sur un fichier.</summary>
    public FileKey? Subject { get; init; }

    /// <summary>Second chemin implique : ancien nom lors d'un renommage, cible d'un raccourci.</summary>
    public string? SecondaryPath { get; init; }

    /// <summary>Donnees propres au module (PID, run count, numero de serie...).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public required Evidence Evidence { get; init; }

    /// <summary>Origine lisible : « Journal USN (C:) », « HKCU\SOFTWARE\WinRAR\ArcHistory ».</summary>
    public required string Source { get; init; }

    public string? Field(string name) =>
        Fields.TryGetValue(name, out var value) ? value : null;
}
