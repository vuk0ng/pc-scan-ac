using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security.Credentials;

namespace GModForensic.Native.Credentials;

/// <summary>Entree du Gestionnaire d'identifiants — NOMS uniquement, jamais de secret.</summary>
public sealed record CredentialEntry
{
    public required string TargetName { get; init; }
    public string? UserName { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset? LastWritten { get; init; }
    public string? Comment { get; init; }
}

/// <summary>
/// Enumere les entrees du Gestionnaire d'identifiants de l'utilisateur courant.
/// <para>
/// GARANTIE (§17) : les champs <c>CredentialBlobSize</c> et <c>CredentialBlob</c> de la
/// structure ne sont JAMAIS lus. Aucun mot de passe n'est accede, affiche ni exporte.
/// Aucun appel a CredRead, aucune DPAPI, aucun acces a %APPDATA%\Microsoft\Credentials.
/// Un test de la suite verifie qu'aucun code de ce fichier ne nomme ces champs.
/// </para>
/// </summary>
public static class CredentialEnumerator
{
    public static unsafe IReadOnlyList<CredentialEntry> Enumerate()
    {
        uint count = 0;
        CREDENTIALW** credentials = null;

        // Filtre nul : toutes les entrees de l'utilisateur courant.
        if (!PInvoke.CredEnumerate(
                default(Windows.Win32.Foundation.PCWSTR),
                (CRED_ENUMERATE_FLAGS)CRED_ENUMERATE_ALL_CREDENTIALS, &count, &credentials))
        {
            // Aucune entree, ou coffre inaccessible : ce n'est pas une erreur exploitable.
            return [];
        }

        try
        {
            var results = new List<CredentialEntry>((int)count);

            for (uint i = 0; i < count; i++)
            {
                var credential = credentials[i];

                if (credential is null)
                {
                    continue;
                }

                var target = credential->TargetName.ToString();

                if (string.IsNullOrEmpty(target))
                {
                    continue;
                }

                results.Add(new CredentialEntry
                {
                    TargetName = target,
                    UserName = NullIfEmpty(credential->UserName.ToString()),
                    Type = credential->Type.ToString(),
                    LastWritten = ToTimestamp(credential->LastWritten),
                    Comment = NullIfEmpty(credential->Comment.ToString()),
                });
            }

            return results;
        }
        finally
        {
            PInvoke.CredFree(credentials);
        }
    }

    private const uint CRED_ENUMERATE_ALL_CREDENTIALS = 0x1;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset? ToTimestamp(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
    {
        var value = ((long)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;

        return value <= 0 ? null : DateTimeOffset.FromFileTime(value).ToUniversalTime();
    }
}
