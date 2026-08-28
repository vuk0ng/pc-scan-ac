namespace GModForensic.Abstractions.Model;

/// <summary>
/// Trace brute permettant au staff de refaire la verification a la main (§1).
/// Aucun binaire suspect n'est copie : hashes et metadonnees uniquement.
/// </summary>
public sealed record Evidence
{
    /// <summary>« RegistryValue », « UsnRecord », « PrefetchEntry », « MemoryString »...</summary>
    public required string Kind { get; init; }

    /// <summary>Ou re-regarder : chemin de cle, offset et USN, PID + adresse de region...</summary>
    public required string Locator { get; init; }

    /// <summary>Valeur brute, tronquee si necessaire (voir <see cref="OriginalLength"/>).</summary>
    public string? RawText { get; init; }

    /// <summary>Longueur d'origine avant troncature, pour que la troncature soit visible.</summary>
    public int? OriginalLength { get; init; }

    /// <summary>Octets bruts, exportes en hexadecimal dans le rapport. Plafonnes.</summary>
    public IReadOnlyList<byte>? RawBytes { get; init; }

    /// <summary>Indication concrete de verification manuelle, redigee pour un non-developpeur.</summary>
    public string? VerificationHint { get; init; }

    public const int MaxRawTextLength = 4096;
    public const int MaxRawByteCount = 1024;

    /// <summary>Tronque un texte brut en conservant sa longueur d'origine.</summary>
    public static Evidence FromText(string kind, string locator, string text, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var truncated = text.Length > MaxRawTextLength
            ? text[..MaxRawTextLength]
            : text;

        return new Evidence
        {
            Kind = kind,
            Locator = locator,
            RawText = truncated,
            OriginalLength = text.Length,
            VerificationHint = hint,
        };
    }
}
