using System.Buffers.Binary;
using System.Text;

namespace GModForensic.Parsers;

/// <summary>Decodeurs des valeurs binaires rencontrees dans les artefacts d'execution.</summary>
public static class RegistryValueDecoders
{
    /// <summary>
    /// BAM — les 8 premiers octets d'une valeur sont un FILETIME UTC : la derniere execution
    /// de l'executable dont le chemin NT sert de nom de valeur.
    /// </summary>
    public static DateTimeOffset? ReadBamTimestamp(ReadOnlySpan<byte> value)
    {
        if (value.Length < sizeof(long))
        {
            return null;
        }

        return FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(value));
    }

    /// <summary>
    /// UserAssist — structure « Count » de Windows 7 et suivants : 72 octets, dont le nombre
    /// d'executions a l'offset 4 et un FILETIME a l'offset 60.
    /// </summary>
    public static (int RunCount, DateTimeOffset? LastRun)? ReadUserAssistCount(ReadOnlySpan<byte> value)
    {
        const int Win7StructSize = 72;
        const int RunCountOffset = 4;
        const int TimestampOffset = 60;

        if (value.Length < Win7StructSize)
        {
            return null;
        }

        var runCount = BinaryPrimitives.ReadInt32LittleEndian(value[RunCountOffset..]);
        var lastRun = FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(value[TimestampOffset..]));

        return (runCount < 0 ? 0 : runCount, lastRun);
    }

    /// <summary>
    /// UserAssist — les noms de valeur sont encodes en ROT13. Ce n'est pas du chiffrement,
    /// seulement une obfuscation triviale : seules les lettres ASCII sont decalees.
    /// </summary>
    public static string DecodeRot13(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return string.Create(value.Length, value, static (destination, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];

                destination[i] = c switch
                {
                    >= 'a' and <= 'z' => (char)('a' + ((c - 'a' + 13) % 26)),
                    >= 'A' and <= 'Z' => (char)('A' + ((c - 'A' + 13) % 26)),
                    _ => c,
                };
            }
        });
    }

    /// <summary>
    /// 7-Zip — l'historique d'archives est une valeur binaire : des chaines UTF-16LE mises
    /// bout a bout et separees par un caractere nul.
    /// </summary>
    public static IReadOnlyList<string> ReadUtf16StringList(ReadOnlySpan<byte> value)
    {
        var results = new List<string>();

        if (value.Length < 2)
        {
            return results;
        }

        var builder = new StringBuilder();

        for (var offset = 0; offset + 1 < value.Length; offset += 2)
        {
            var unit = BinaryPrimitives.ReadUInt16LittleEndian(value[offset..]);

            if (unit == 0)
            {
                Flush(results, builder);
                continue;
            }

            builder.Append((char)unit);
        }

        Flush(results, builder);
        return results;

        static void Flush(List<string> target, StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                target.Add(builder.ToString());
                builder.Clear();
            }
        }
    }

    /// <summary>Convertit un FILETIME, en rejetant les valeurs nulles ou hors plage.</summary>
    public static DateTimeOffset? FromFileTime(long fileTime)
    {
        if (fileTime <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Une valeur aberrante n'est pas une date : mieux vaut « inconnue » qu'une fausse date.
            return null;
        }
    }
}
