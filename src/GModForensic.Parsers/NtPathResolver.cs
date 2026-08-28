using System.Diagnostics.CodeAnalysis;

namespace GModForensic.Parsers;

/// <summary>
/// Ramene les differentes ecritures d'un chemin Windows a une forme unique.
/// <para>
/// Indispensable a la correlation : le journal USN, BAM, le Prefetch et la memoire
/// designent le meme fichier avec quatre ecritures differentes.
/// </para>
/// </summary>
public static class NtPathResolver
{
    private const string DevicePrefix = @"\Device\";
    private const string FileUriPrefix = "file:///";

    /// <summary>
    /// Normalise un chemin. <paramref name="deviceMap"/> associe un nom de peripherique NT
    /// (« \Device\HarddiskVolume2 ») a sa lettre de volume (« C: ») ; il provient de
    /// QueryDosDevice, et reste vide dans les tests.
    /// </summary>
    public static string Normalize(string path, IReadOnlyDictionary<string, string>? deviceMap = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var value = path.Trim();

        // file:///C:/Users/... — forme rencontree en memoire et dans les raccourcis.
        if (value.StartsWith(FileUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = Uri.UnescapeDataString(value[FileUriPrefix.Length..]).Replace('/', '\\');
        }

        // \??\C:\... et \\?\C:\... — prefixes d'espace de noms NT.
        if (value.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            value = value[4..];
        }
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)
                 && !value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        // \Device\HarddiskVolume2\Users\... — forme de BAM et des journaux noyau.
        if (value.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase) && deviceMap is not null)
        {
            var rest = value[DevicePrefix.Length..];
            var separator = rest.IndexOf('\\');
            var deviceName = separator < 0 ? rest : rest[..separator];
            var tail = separator < 0 ? string.Empty : rest[separator..];

            if (deviceMap.TryGetValue(DevicePrefix + deviceName, out var drive))
            {
                value = drive + tail;
            }
        }

        return value.TrimEnd('\\', ' ');
    }

    /// <summary>Indique si le chemin designe un dossier temporaire ou de telechargement utilisateur.</summary>
    public static bool IsUserVolatilePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        ReadOnlySpan<string> markers =
        [
            @"\appdata\local\temp", @"\downloads", @"\windows\temp", @"\appdata\roaming",
        ];

        var lower = path.ToLowerInvariant();

        foreach (var marker in markers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Extrait le nom de fichier d'un chemin, quelle que soit sa forme.</summary>
    public static bool TryGetFileName(string path, [NotNullWhen(true)] out string? fileName)
    {
        fileName = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var index = path.LastIndexOfAny(['\\', '/']);
        var candidate = index < 0 ? path : path[(index + 1)..];

        if (candidate.Length == 0)
        {
            return false;
        }

        fileName = candidate;
        return true;
    }
}
