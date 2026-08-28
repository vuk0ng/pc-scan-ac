using System.Runtime.InteropServices;
using Windows.Win32;

namespace GModForensic.Native.Storage;

/// <summary>
/// Associe chaque nom de peripherique NT (« \Device\HarddiskVolume2 ») a sa lettre de volume.
/// <para>
/// BAM, les journaux noyau et la memoire designent les fichiers sous forme de peripherique :
/// sans cette table, leurs chemins ne peuvent pas etre rapproches de ceux du disque.
/// </para>
/// </summary>
public static class VolumeMap
{
    /// <summary>Construit la table. Retourne une table vide plutot que d'echouer.</summary>
    public static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            var letter = drive.Name.TrimEnd('\\');

            if (letter.Length != 2 || letter[1] != ':')
            {
                continue;
            }

            var device = QueryDosDevice(letter);

            if (device is not null)
            {
                map[device] = letter;
            }
        }

        return map;
    }

    private static unsafe string? QueryDosDevice(string driveLetter)
    {
        const int BufferLength = 1024;
        var buffer = stackalloc char[BufferLength];

        var written = PInvoke.QueryDosDevice(driveLetter, buffer, BufferLength);

        if (written == 0)
        {
            return null;
        }

        // QueryDosDevice renvoie une liste terminee par un double nul : seule la premiere
        // entree nous interesse.
        var value = new string(buffer, 0, (int)written).Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return value.Length > 0 ? value[0] : null;
    }
}
