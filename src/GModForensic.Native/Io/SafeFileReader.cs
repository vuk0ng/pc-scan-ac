using System.Security.Cryptography;

namespace GModForensic.Native.Io;

/// <summary>
/// Point d'entree UNIQUE de toute lecture de fichier du produit.
/// <para>
/// Les surcharges de <c>System.IO.File.Open</c> sont interdites par BannedSymbols.txt afin que
/// chaque lecture passe obligatoirement ici, avec les memes garanties :
/// </para>
/// <list type="bullet">
///   <item><description><see cref="FileAccess.Read"/> — le fichier analyse n'est jamais modifie ;</description></item>
///   <item><description><see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/> — le fichier
///   n'est jamais verrouille, et reste supprimable par son proprietaire pendant l'analyse ;</description></item>
///   <item><description><see cref="FileMode.Open"/> — aucune creation implicite.</description></item>
/// </list>
/// </summary>
public static class SafeFileReader
{
    /// <summary>Partage systematique : ne jamais bloquer le systeme analyse.</summary>
    public const FileShare ForensicShare = FileShare.ReadWrite | FileShare.Delete;

    private const int BufferSize = 64 * 1024;

    /// <summary>Ouvre un fichier en lecture seule non bloquante. L'appelant dispose du flux.</summary>
    public static FileStream Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            ForensicShare,
            BufferSize,
            FileOptions.SequentialScan);
    }

    /// <summary>
    /// Calcule le SHA-256 d'un fichier sans jamais le charger entierement en memoire.
    /// Retourne <c>null</c> si le fichier depasse <paramref name="maxBytes"/> : au-dela,
    /// seules les metadonnees sont conservees (§26).
    /// </summary>
    public static string? TryComputeSha256(string path, long maxBytes, CancellationToken cancellationToken)
    {
        using var stream = Open(path);

        if (stream.Length > maxBytes)
        {
            return null;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        cancellationToken.ThrowIfCancellationRequested();

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Lit au plus <paramref name="maxBytes"/> octets. Utilise par les parseurs binaires.</summary>
    public static byte[] ReadBytes(string path, int maxBytes)
    {
        using var stream = Open(path);

        var length = (int)Math.Min(stream.Length, maxBytes);
        var buffer = new byte[length];
        stream.ReadExactly(buffer, 0, length);

        return buffer;
    }
}
