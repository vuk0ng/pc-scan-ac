using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace GModForensic.Native.Security;

/// <summary>
/// Mesure ce que le processus peut REELLEMENT faire.
/// <para>
/// Le manifeste <c>requireAdministrator</c> garantit l'elevation, mais pas qu'un privilege donne
/// soit actif. Le §2 exigeant d'afficher clairement les verifications impossibles, cette mesure
/// runtime est la seule facon honnete de le faire (limite L12 de docs/01).
/// </para>
/// </summary>
public static class TokenInspector
{
    public const string DebugPrivilege = "SeDebugPrivilege";
    public const string SecurityPrivilege = "SeSecurityPrivilege";

    /// <summary>
    /// Elevation reelle du jeton. <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> est
    /// utilise volontairement : sur un jeton filtre par l'UAC il retourne <c>false</c>, ce qui est
    /// exactement le comportement attendu.
    /// </summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    public static string CurrentUserName()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return Environment.UserName;
        }
    }

    /// <summary>
    /// Indique si un privilege est present ET actif dans le jeton du processus courant.
    /// <para>
    /// <c>PrivilegeCheck</c> n'est deliberement pas utilise : cette API exige un jeton
    /// d'usurpation et echoue sur un jeton primaire. On lit donc directement
    /// <c>TokenPrivileges</c> et on compare les LUID.
    /// </para>
    /// </summary>
    public static bool IsPrivilegeEnabled(string privilegeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(privilegeName);

        if (!TryGetPrivilegeLuid(privilegeName, out var luidLow, out var luidHigh))
        {
            return false;
        }

        var buffer = TryReadTokenPrivileges();
        if (buffer is null)
        {
            return false;
        }

        foreach (var entry in EnumeratePrivileges(buffer))
        {
            if (entry.LuidLow == luidLow && entry.LuidHigh == luidHigh)
            {
                return (entry.Attributes & (uint)TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED) != 0;
            }
        }

        return false;
    }

    /// <summary>Une entree de la structure TOKEN_PRIVILEGES, decodee sans dependre d'un layout genere.</summary>
    internal readonly record struct PrivilegeEntry(uint LuidLow, int LuidHigh, uint Attributes);

    /// <summary>
    /// Decode <c>TOKEN_PRIVILEGES</c> : un <c>uint</c> de comptage, puis N structures
    /// <c>LUID_AND_ATTRIBUTES</c> de 12 octets (LUID = uint + int, puis attributs).
    /// Fonction pure, donc verifiable sans Windows.
    /// </summary>
    internal static IReadOnlyList<PrivilegeEntry> EnumeratePrivileges(ReadOnlySpan<byte> buffer)
    {
        const int EntrySize = 12;

        if (buffer.Length < sizeof(uint))
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var available = (buffer.Length - sizeof(uint)) / EntrySize;
        var usable = (int)Math.Min(count, (uint)available);

        var entries = new List<PrivilegeEntry>(usable);

        for (var i = 0; i < usable; i++)
        {
            var offset = sizeof(uint) + (i * EntrySize);
            entries.Add(new PrivilegeEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]),
                BinaryPrimitives.ReadInt32LittleEndian(buffer[(offset + 4)..]),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 8)..])));
        }

        return entries;
    }

    private static bool TryGetPrivilegeLuid(string name, out uint low, out int high)
    {
        low = 0;
        high = 0;

        if (!PInvoke.LookupPrivilegeValue(null, name, out var luid))
        {
            return false;
        }

        low = luid.LowPart;
        high = luid.HighPart;
        return true;
    }

    private static unsafe byte[]? TryReadTokenPrivileges()
    {
        using var token = OpenCurrentProcessToken();
        if (token is null || token.IsInvalid)
        {
            return null;
        }

        var handle = new HANDLE(token.DangerousGetHandle());

        uint required = 0;
        PInvoke.GetTokenInformation(handle, TOKEN_INFORMATION_CLASS.TokenPrivileges, null, 0, &required);

        if (required == 0)
        {
            return null;
        }

        var buffer = new byte[required];

        fixed (byte* pointer = buffer)
        {
            uint written = 0;
            if (!PInvoke.GetTokenInformation(
                    handle, TOKEN_INFORMATION_CLASS.TokenPrivileges, pointer, required, &written))
            {
                return null;
            }
        }

        return buffer;
    }

    private static unsafe SafeHandle? OpenCurrentProcessToken()
    {
        var process = PInvoke.GetCurrentProcess_SafeHandle();

        return PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)
            ? token
            : null;
    }
}
