namespace GModForensic.Abstractions.Model;

/// <summary>
/// Cle de correlation d'une entite fichier.
/// <para>
/// C'est la piece centrale du moteur : un meme fichier est vu par le journal USN via une
/// reference MFT, par le Prefetch via un nom et un hash de chemin, par BAM via un chemin NT,
/// par la memoire via un chemin litteral et par le disque via un SHA-256. Les fusionner est
/// ce qui permet la phrase « execute a 21 h 12, renomme a 21 h 14, supprime a 21 h 15 ».
/// </para>
/// </summary>
public sealed record FileKey
{
    /// <summary>Nom de fichier seul, toujours disponible. Compare en invariant, insensible a la casse.</summary>
    public required string FileName { get; init; }

    /// <summary>Chemin complet normalise (lettre de volume, casse repliee, variables resolues), si connu.</summary>
    public string? FullPath { get; init; }

    /// <summary>SHA-256 minuscule sans separateur, si le fichier existe encore et a pu etre lu.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Numero de reference MFT, si le fait provient du journal USN.</summary>
    public ulong? MftReference { get; init; }

    /// <summary>Lettre de volume d'origine (« C »), qui donne son sens a <see cref="MftReference"/>.</summary>
    public string? Volume { get; init; }

    /// <summary>Hash de chemin Prefetch (suffixe du nom de fichier .pf), si le fait provient du Prefetch.</summary>
    public string? PrefetchHash { get; init; }

    public string Extension =>
        Path.GetExtension(FileName).ToLowerInvariant();

    /// <summary>
    /// Deux cles designent la meme entite si elles partagent un identifiant fort :
    /// meme SHA-256, meme chemin complet, ou meme reference MFT sur le meme volume.
    /// Le nom seul ne suffit jamais.
    /// </summary>
    public bool IsSameEntityAs(FileKey other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Sha256 is not null && other.Sha256 is not null)
        {
            return string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        if (FullPath is not null && other.FullPath is not null)
        {
            return string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        if (MftReference is not null && other.MftReference is not null && Volume is not null)
        {
            return MftReference == other.MftReference
                && string.Equals(Volume, other.Volume, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public override string ToString() => FullPath ?? FileName;
}
