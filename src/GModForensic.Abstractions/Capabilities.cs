using GModForensic.Abstractions.Model;

namespace GModForensic.Abstractions;

/// <summary>Ce dont un module a besoin pour pouvoir s'executer utilement.</summary>
[Flags]
public enum RequiredCapabilities
{
    None = 0,
    Administrator = 1 << 0,
    /// <summary>Handle sur un volume NTFS brut (journal USN).</summary>
    NtfsVolume = 1 << 1,
    /// <summary>Lecture de la memoire d'autres processus.</summary>
    ProcessMemory = 1 << 2,
    /// <summary>Canal Security de l'observateur d'evenements.</summary>
    SecurityEventLog = 1 << 3,
    /// <summary>Lecture de C:\Windows\Prefetch.</summary>
    PrefetchFolder = 1 << 4,
    /// <summary>Coffre d'identifiants de l'utilisateur courant.</summary>
    UserCredentialVault = 1 << 5,
}

/// <summary>
/// Ce que le processus peut REELLEMENT faire, mesure au demarrage.
/// <para>
/// Le §2 demande d'afficher clairement quelles verifications ne pourront pas etre effectuees.
/// Cette mesure runtime est la seule facon honnete de le faire : le manifeste
/// <c>requireAdministrator</c> garantit l'elevation, mais pas que chaque privilege soit
/// effectivement actif (voir la limite L12 de docs/01).
/// </para>
/// </summary>
public sealed record Capabilities
{
    public required bool IsElevated { get; init; }
    public required bool HasDebugPrivilege { get; init; }
    public required bool HasSecurityPrivilege { get; init; }
    public required bool HasNtfsVolume { get; init; }
    public required bool PrefetchFolderReadable { get; init; }
    public required bool CanReadProcessMemory { get; init; }
    public required bool UserCredentialVaultAccessible { get; init; }

    /// <summary>Precisions affichees au staff (nom du compte, volumes NTFS trouves...).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// Retourne le motif de blocage a afficher, ou <c>null</c> si la capacite est satisfaite.
    /// Le texte est destine a l'ecran d'accueil et au rapport, pas a un journal technique.
    /// </summary>
    public string? ExplainMissing(RequiredCapabilities required)
    {
        var missing = new List<string>();

        if (required.HasFlag(RequiredCapabilities.Administrator) && !IsElevated)
        {
            missing.Add("privileges administrateur");
        }

        if (required.HasFlag(RequiredCapabilities.NtfsVolume) && !HasNtfsVolume)
        {
            missing.Add("aucun volume NTFS accessible");
        }

        if (required.HasFlag(RequiredCapabilities.ProcessMemory) && !CanReadProcessMemory)
        {
            missing.Add("lecture memoire indisponible (SeDebugPrivilege absent)");
        }

        if (required.HasFlag(RequiredCapabilities.SecurityEventLog) && !HasSecurityPrivilege)
        {
            missing.Add("journal Security inaccessible (SeSecurityPrivilege absent)");
        }

        if (required.HasFlag(RequiredCapabilities.PrefetchFolder) && !PrefetchFolderReadable)
        {
            missing.Add("dossier Prefetch illisible ou desactive");
        }

        if (required.HasFlag(RequiredCapabilities.UserCredentialVault) && !UserCredentialVaultAccessible)
        {
            missing.Add("coffre d'identifiants inaccessible");
        }

        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    /// <summary>Capacites d'un environnement sans aucun privilege — utilise en tests et en mode degrade.</summary>
    public static Capabilities None { get; } = new()
    {
        IsElevated = false,
        HasDebugPrivilege = false,
        HasSecurityPrivilege = false,
        HasNtfsVolume = false,
        PrefetchFolderReadable = false,
        CanReadProcessMemory = false,
        UserCredentialVaultAccessible = false,
    };
}
