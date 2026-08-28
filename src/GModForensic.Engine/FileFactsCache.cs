using System.Collections.Concurrent;
using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>
/// Cache partage par tous les modules : un fichier n'est jamais hashe ni verifie deux fois (§26).
/// </summary>
public sealed class FileFactsCache : IFileFactsCache
{
    private readonly IFileFactsProvider _provider;
    private readonly ConcurrentDictionary<string, FileFacts> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public FileFactsCache(IFileFactsProvider provider) => _provider = provider;

    public int Count => _cache.Count;

    public FileFacts Get(string path, bool computeHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (_cache.TryGetValue(path, out var cached)
            && (!computeHash || cached.Sha256 is not null || !cached.Exists))
        {
            return cached;
        }

        // Une entree lue sans hash est remplacee si le hash devient necessaire.
        var facts = _provider.Read(path, computeHash, cancellationToken);
        _cache[path] = facts;
        return facts;
    }
}

/// <summary>
/// Fournisseur inerte : declare tout fichier absent, sans jamais toucher au disque.
/// Utilise en test et sur les plateformes non-Windows ; remplace a l'etape 5 par
/// l'implementation Win32 de GModForensic.Native.
/// </summary>
public sealed class NullFileFactsProvider : IFileFactsProvider
{
    public FileFacts Read(string path, bool computeHash, CancellationToken cancellationToken) =>
        FileFacts.Missing(path) with { Limitation = "Aucun fournisseur de metadonnees installe." };
}
