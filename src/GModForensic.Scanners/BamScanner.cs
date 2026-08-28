using System.Globalization;
using System.Security.Principal;
using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Native.Storage;
using GModForensic.Parsers;
using Microsoft.Win32;

namespace GModForensic.Scanners;

/// <summary>
/// M09 — Background Activity Moderator (§12).
/// <para>
/// L'un des rares artefacts a porter un chemin COMPLET et une date fiable de derniere
/// execution. Il sert donc de source d'attribution pour les entrees Prefetch homonymes.
/// Sa fenetre est courte — environ sept jours — et l'emplacement de la cle varie selon la
/// version de Windows : les deux sont essayes.
/// </para>
/// </summary>
public sealed class BamScanner : IScanModule
{
    private static readonly string[] Roots =
    [
        @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings",
        @"SYSTEM\CurrentControlSet\Services\bam\UserSettings",
    ];

    public string Id => "bam";

    public string DisplayName => "BAM — dernieres executions";

    public ScanCategory Category => ScanCategory.Registry;

    public RequiredCapabilities Requires => RequiredCapabilities.Administrator;

    public int Weight => 4;

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ModuleRun(Id, context);
        var devices = VolumeMap.Build();

        run.Progress(0.1, "Recherche de la cle BAM");

        var found = false;

        foreach (var root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var key = run.OpenKey(RegistryHive.LocalMachine, root);

            if (key is null)
            {
                continue;
            }

            found = true;
            ReadUsers(run, key, root, devices, cancellationToken);
            break;
        }

        run.Progress(1, "Termine");

        return Task.FromResult(found
            ? run.ToResult()
            : run.ToResult("Cle BAM absente sur cette version de Windows."));
    }

    private void ReadUsers(
        ModuleRun run,
        RegistryKey root,
        string rootPath,
        IReadOnlyDictionary<string, string> devices,
        CancellationToken cancellationToken)
    {
        var sids = root.GetSubKeyNames();

        for (var i = 0; i < sids.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sid = sids[i];
            using var userKey = root.OpenSubKey(sid);

            if (userKey is null)
            {
                continue;
            }

            var account = ResolveAccount(sid);
            run.Progress(0.1 + (0.85 * (i + 1) / sids.Length), $"BAM — {account}");

            foreach (var name in userKey.GetValueNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (userKey.GetValue(name) is not byte[] raw)
                {
                    continue;
                }

                var timestamp = RegistryValueDecoders.ReadBamTimestamp(raw);

                if (timestamp is null)
                {
                    continue;
                }

                // Le nom de valeur est un chemin NT : sans la table des volumes il ne peut
                // pas etre rapproche de ce qui est vu sur le disque.
                var path = NtPathResolver.Normalize(name, devices);

                if (!NtPathResolver.TryGetFileName(path, out var fileName))
                {
                    continue;
                }

                run.Add(new Observation
                {
                    ModuleId = Id,
                    Kind = ObservationKind.ExecutionRecord,
                    Timestamp = timestamp,
                    Subject = new FileKey
                    {
                        FileName = fileName,
                        FullPath = path.Contains('\\', StringComparison.Ordinal) ? path : null,
                    },
                    Fields = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["artifact"] = "BAM",
                        ["sid"] = sid,
                        ["account"] = account,
                        ["ntPath"] = name,
                        ["window"] = "BAM est purge apres environ 7 jours",
                    },
                    Source = $@"HKLM\{rootPath}\{sid}",
                    Evidence = Evidence.FromText("RegistryValue", $@"HKLM\{rootPath}\{sid} → {name}",
                        $"{timestamp:O} — {path}",
                        "Les 8 premiers octets de la valeur sont un FILETIME UTC."),
                });
            }
        }
    }

    private static string ResolveAccount(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is ArgumentException or IdentityNotMappedException or SystemException)
        {
            // Un SID non resoluble (compte supprime, machine hors domaine) n'est pas une erreur.
            return sid;
        }
    }
}
