using GModForensic.Abstractions;

namespace GModForensic.Native.Security;

/// <summary>
/// Construit l'inventaire des capacites au demarrage du programme, avant tout scan.
/// Les modules dont les capacites ne sont pas satisfaites sont annonces comme ignores,
/// avec leur motif — exigence du §2.
/// </summary>
public static class CapabilityProbe
{
    public static Capabilities Measure()
    {
        var notes = new List<string>();

        var elevated = TokenInspector.IsElevated();
        var debug = TokenInspector.IsPrivilegeEnabled(TokenInspector.DebugPrivilege);
        var security = TokenInspector.IsPrivilegeEnabled(TokenInspector.SecurityPrivilege);

        notes.Add($"Compte : {TokenInspector.CurrentUserName()}");

        var ntfsVolumes = FindNtfsVolumes();
        notes.Add(ntfsVolumes.Count > 0
            ? $"Volumes NTFS : {string.Join(", ", ntfsVolumes)}"
            : "Aucun volume NTFS detecte — le journal USN sera indisponible.");

        var prefetch = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        var prefetchReadable = elevated && DirectoryIsReadable(prefetch);

        if (!prefetchReadable)
        {
            notes.Add(elevated
                ? "Dossier Prefetch illisible : il est peut-etre desactive."
                : "Dossier Prefetch inaccessible sans elevation.");
        }

        return new Capabilities
        {
            IsElevated = elevated,
            HasDebugPrivilege = debug,
            HasSecurityPrivilege = security,
            HasNtfsVolume = ntfsVolumes.Count > 0,
            PrefetchFolderReadable = prefetchReadable,
            // Lire la memoire d'un processus d'un autre utilisateur suppose SeDebugPrivilege.
            CanReadProcessMemory = elevated && debug,
            UserCredentialVaultAccessible = true,
            Notes = notes,
        };
    }

    private static List<string> FindNtfsVolumes()
    {
        var volumes = new List<string>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady
                    && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    volumes.Add(drive.Name.TrimEnd('\\'));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Un lecteur non pret ou protege n'est pas une erreur : on l'ignore.
            }
        }

        return volumes;
    }

    private static bool DirectoryIsReadable(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
