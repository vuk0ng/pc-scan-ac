namespace GModForensic.Abstractions.Model;

/// <summary>
/// Metadonnees d'un fichier, obtenues en LECTURE SEULE et mises en cache pour l'ensemble
/// du scan : un fichier n'est jamais hashe ni verifie deux fois (§26).
/// </summary>
public sealed record FileFacts
{
    public required string Path { get; init; }

    public required bool Exists { get; init; }

    public long? SizeBytes { get; init; }

    public DateTimeOffset? CreatedUtc { get; init; }

    public DateTimeOffset? ModifiedUtc { get; init; }

    public DateTimeOffset? AccessedUtc { get; init; }

    public FileAttributes? Attributes { get; init; }

    /// <summary><c>null</c> si non calcule (fichier absent, trop volumineux, ou hash non demande).</summary>
    public string? Sha256 { get; init; }

    public SignatureStatus Signature { get; init; } = SignatureStatus.Unknown;

    /// <summary>Sujet du certificat de signature, quand la signature est exploitable.</summary>
    public string? Publisher { get; init; }

    /// <summary>Valeur <c>HostUrl</c> du flux :Zone.Identifier — l'URL source du telechargement (M15).</summary>
    public string? ZoneHostUrl { get; init; }

    /// <summary>Raison pour laquelle une information manque (acces refuse, taille depassee...).</summary>
    public string? Limitation { get; init; }

    public static FileFacts Missing(string path) => new()
    {
        Path = path,
        Exists = false,
    };
}

/// <summary>Source de <see cref="FileFacts"/>. Implementee par la couche Native (Windows).</summary>
public interface IFileFactsProvider
{
    FileFacts Read(string path, bool computeHash, CancellationToken cancellationToken);
}

/// <summary>Cache partage par tous les modules pendant un scan.</summary>
public interface IFileFactsCache
{
    FileFacts Get(string path, bool computeHash, CancellationToken cancellationToken);

    /// <summary>Nombre d'entrees distinctes lues, pour le journal d'execution.</summary>
    int Count { get; }
}
