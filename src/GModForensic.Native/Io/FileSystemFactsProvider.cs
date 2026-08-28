using GModForensic.Abstractions.Model;

namespace GModForensic.Native.Io;

/// <summary>
/// Metadonnees de fichier obtenues en lecture seule.
/// <para>
/// Version de l'etape 4 : taille, dates, attributs et SHA-256. La verification Authenticode
/// et la lecture du flux Zone.Identifier arrivent avec les modules (etape 5) ; en attendant,
/// <see cref="FileFacts.Limitation"/> le dit explicitement plutot que de laisser croire a une
/// absence de signature.
/// </para>
/// </summary>
public sealed class FileSystemFactsProvider : IFileFactsProvider
{
    private readonly long _maxHashBytes;

    public FileSystemFactsProvider(long maxHashBytes) => _maxHashBytes = maxHashBytes;

    public FileFacts Read(string path, bool computeHash, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return FileFacts.Missing(path);
            }

            string? hash = null;
            string? limitation = "Signature non evaluee a ce stade (etape 5).";

            if (computeHash)
            {
                if (info.Length > _maxHashBytes)
                {
                    limitation = $"Fichier de {info.Length / (1024 * 1024)} Mo : hash non calcule.";
                }
                else
                {
                    hash = SafeFileReader.TryComputeSha256(path, _maxHashBytes, cancellationToken);
                }
            }

            return new FileFacts
            {
                Path = path,
                Exists = true,
                SizeBytes = info.Length,
                CreatedUtc = info.CreationTimeUtc,
                ModifiedUtc = info.LastWriteTimeUtc,
                AccessedUtc = info.LastAccessTimeUtc,
                Attributes = info.Attributes,
                Sha256 = hash,
                Limitation = limitation,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Un fichier illisible n'interrompt jamais un scan : l'obstacle est decrit, pas masque.
            return FileFacts.Missing(path) with { Limitation = $"Lecture impossible : {ex.Message}" };
        }
    }
}
