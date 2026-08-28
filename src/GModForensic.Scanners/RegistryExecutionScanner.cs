using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Parsers;
using Microsoft.Win32;

namespace GModForensic.Scanners;

/// <summary>
/// M08 — programmes utilises, d'apres les artefacts d'execution du registre (§12).
/// <para>
/// Lecture STRICTEMENT en lecture seule. Chaque source a une portee et une fiabilite
/// differentes, indiquees dans les observations : MuiCache n'a aucun horodatage par entree,
/// UserAssist en a un, FeatureUsage ne couvre que la barre des taches.
/// </para>
/// </summary>
public sealed class RegistryExecutionScanner : IScanModule
{
    public string Id => "registry-exec";

    public string DisplayName => "Registre — programmes utilises";

    public ScanCategory Category => ScanCategory.Registry;

    public RequiredCapabilities Requires => RequiredCapabilities.None;

    public int Weight => 5;

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ModuleRun(Id, context);
        var now = context.Clock.GetUtcNow();

        run.Progress(0.1, "Compatibility Assistant");
        ReadCompatibilityAssistant(run, now, cancellationToken);

        run.Progress(0.35, "MuiCache");
        ReadMuiCache(run, now, cancellationToken);

        run.Progress(0.6, "FeatureUsage");
        ReadFeatureUsage(run, now, cancellationToken);

        run.Progress(0.8, "UserAssist");
        ReadUserAssist(run, now, cancellationToken);

        run.Progress(1, "Termine");
        return Task.FromResult(run.ToResult());
    }

    private void ReadCompatibilityAssistant(ModuleRun run, DateTimeOffset now, CancellationToken ct)
    {
        using var key = run.OpenKey(RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store");

        if (key is null)
        {
            return;
        }

        const string source = @"HKCU\...\AppCompatFlags\Compatibility Assistant\Store";

        foreach (var name in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();

            if (!name.Contains('\\', StringComparison.Ordinal))
            {
                continue;
            }

            run.Add(Execution(now, name, source, ObservationKind.ExecutionRecord, new()
            {
                ["artifact"] = "CompatibilityAssistant",
                // La valeur est une structure binaire opaque : aucune date exploitable.
                ["timestampAvailable"] = "non",
            }));
        }
    }

    private void ReadMuiCache(ModuleRun run, DateTimeOffset now, CancellationToken ct)
    {
        using var key = run.OpenKey(RegistryHive.CurrentUser,
            @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");

        if (key is null)
        {
            return;
        }

        const string source = @"HKCU\...\Shell\MuiCache";

        foreach (var name in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();

            // Les noms sont de la forme « C:\chemin\x.exe.FriendlyAppName ».
            var separator = name.LastIndexOf('.');

            if (separator <= 0 || !name.Contains('\\', StringComparison.Ordinal))
            {
                continue;
            }

            var path = name[..separator];
            var attribute = name[(separator + 1)..];

            if (!attribute.Equals("FriendlyAppName", StringComparison.OrdinalIgnoreCase)
                && !attribute.Equals("ApplicationCompany", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            run.Add(Execution(now, path, source, ObservationKind.RegistryValue, new()
            {
                ["artifact"] = "MuiCache",
                [attribute] = key.GetValue(name)?.ToString() ?? string.Empty,
                // Piege classique : la cle porte un LastWriteTime global, jamais par entree.
                ["timestampAvailable"] = "non",
            }));
        }
    }

    private void ReadFeatureUsage(ModuleRun run, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var leaf in new[] { "AppLaunch", "AppSwitched" })
        {
            using var key = run.OpenKey(RegistryHive.CurrentUser,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\{leaf}");

            if (key is null)
            {
                continue;
            }

            var source = $@"HKCU\...\Explorer\FeatureUsage\{leaf}";

            foreach (var name in key.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();

                run.Add(Execution(now, name, source, ObservationKind.ExecutionRecord, new()
                {
                    ["artifact"] = $"FeatureUsage/{leaf}",
                    ["count"] = key.GetValue(name)?.ToString() ?? "0",
                    // Ne couvre que ce qui passe par la barre des taches.
                    ["scope"] = "barre des taches uniquement",
                    ["timestampAvailable"] = "non",
                }));
            }
        }
    }

    private void ReadUserAssist(ModuleRun run, DateTimeOffset now, CancellationToken ct)
    {
        using var root = run.OpenKey(RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");

        if (root is null)
        {
            return;
        }

        foreach (var guid in root.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();

            using var counts = root.OpenSubKey($@"{guid}\Count");

            if (counts is null)
            {
                continue;
            }

            var source = $@"HKCU\...\Explorer\UserAssist\{guid}\Count";

            foreach (var name in counts.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();

                // Les noms sont encodes en ROT13 — obfuscation triviale, pas du chiffrement.
                var decoded = RegistryValueDecoders.DecodeRot13(name);

                if (counts.GetValue(name) is not byte[] raw)
                {
                    continue;
                }

                var parsed = RegistryValueDecoders.ReadUserAssistCount(raw);

                var fields = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["artifact"] = "UserAssist",
                    ["runCount"] = parsed?.RunCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?",
                    // UserAssist est le seul de cette famille a porter une date fiable.
                    ["timestampAvailable"] = parsed?.LastRun is null ? "non" : "oui",
                };

                run.Add(new Observation
                {
                    ModuleId = Id,
                    Kind = ObservationKind.ExecutionRecord,
                    Timestamp = parsed?.LastRun,
                    Subject = ToFileKey(decoded),
                    Fields = fields,
                    Source = source,
                    Evidence = Evidence.FromText("RegistryValue", $"{source} → {name}", decoded,
                        "Nom encode en ROT13 dans le registre ; la valeur affichee est decodee."),
                });
            }
        }
    }

    private Observation Execution(
        DateTimeOffset now,
        string path,
        string source,
        ObservationKind kind,
        Dictionary<string, string> fields) => new()
        {
            ModuleId = Id,
            Kind = kind,
            // Pas de date : ces artefacts n'en portent pas par entree. Mettre l'heure du scan
            // laisserait croire a une execution recente.
            Timestamp = null,
            Subject = ToFileKey(path),
            Fields = fields,
            Source = source,
            Evidence = Evidence.FromText("RegistryValue", $"{source} → {path}", path,
                $"Verifiable dans regedit : {source}"),
        };

    private static FileKey? ToFileKey(string path)
    {
        var normalized = NtPathResolver.Normalize(path);

        return NtPathResolver.TryGetFileName(normalized, out var fileName)
            ? new FileKey
            {
                FileName = fileName,
                FullPath = normalized.Contains('\\', StringComparison.Ordinal) ? normalized : null,
            }
            : null;
    }
}
