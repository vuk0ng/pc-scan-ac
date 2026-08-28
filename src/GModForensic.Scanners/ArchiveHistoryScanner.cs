using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Parsers;
using Microsoft.Win32;

namespace GModForensic.Scanners;

/// <summary>
/// M11 — historique d'archives (§14), WinRAR et 7-Zip.
/// <para>
/// L'historique d'EXTRACTION vaut mieux que celui d'ouverture : il indique ou le contenu a
/// ete depose, ce qui oriente l'analyse du systeme de fichiers. Aucune archive n'est ouverte
/// ni extraite.
/// </para>
/// </summary>
public sealed class ArchiveHistoryScanner : IScanModule
{
    public string Id => "archives";

    public string DisplayName => "Historique d'archives";

    public ScanCategory Category => ScanCategory.Archives;

    public RequiredCapabilities Requires => RequiredCapabilities.None;

    public int Weight => 2;

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ModuleRun(Id, context);

        run.Progress(0.2, "WinRAR");
        ReadTextHistory(run, @"SOFTWARE\WinRAR\ArcHistory", "WinRAR", "archive ouverte", cancellationToken);
        ReadTextHistory(run, @"SOFTWARE\WinRAR\DialogEditHistory\ExtrPath", "WinRAR", "dossier d'extraction", cancellationToken);

        run.Progress(0.6, "7-Zip");
        ReadBinaryHistory(run, @"SOFTWARE\7-Zip\Compression", "ArcHistory", "7-Zip", "archive ouverte", cancellationToken);
        ReadBinaryHistory(run, @"SOFTWARE\7-Zip\FM", "FolderHistory", "7-Zip", "dossier parcouru", cancellationToken);

        run.Progress(1, "Termine");
        return Task.FromResult(run.ToResult());
    }

    private void ReadTextHistory(
        ModuleRun run, string path, string tool, string role, CancellationToken cancellationToken)
    {
        using var key = run.OpenKey(RegistryHive.CurrentUser, path);

        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = key.GetValue(name)?.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // Le nom de valeur porte le rang : « 0 » est l'entree la plus recente.
            run.Add(Entry(value, tool, role, name, $@"HKCU\{path}"));
        }
    }

    private void ReadBinaryHistory(
        ModuleRun run, string path, string valueName, string tool, string role, CancellationToken cancellationToken)
    {
        using var key = run.OpenKey(RegistryHive.CurrentUser, path);

        if (key is null)
        {
            return;
        }

        if (key.GetValue(valueName) is not byte[] raw)
        {
            return;
        }

        // 7-Zip stocke son historique en binaire : des chaines UTF-16 separees par un nul.
        var entries = RegistryValueDecoders.ReadUtf16StringList(raw);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Add(Entry(entries[i], tool, role,
                i.ToString(System.Globalization.CultureInfo.InvariantCulture), $@"HKCU\{path}\{valueName}"));
        }
    }

    private Observation Entry(string value, string tool, string role, string rank, string source)
    {
        var normalized = NtPathResolver.Normalize(value);
        NtPathResolver.TryGetFileName(normalized, out var fileName);

        return new Observation
        {
            ModuleId = Id,
            Kind = ObservationKind.ArchiveHistoryEntry,
            // Ces cles ne portent aucune date par entree : seul l'ordre est significatif.
            Timestamp = null,
            Subject = fileName is null ? null : new FileKey { FileName = fileName, FullPath = normalized },
            Fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tool"] = tool,
                ["role"] = role,
                ["rank"] = rank,
                ["timestampAvailable"] = "non",
            },
            Source = source,
            Evidence = Evidence.FromText("RegistryValue", $"{source} → {rank}", value,
                $"Verifiable dans regedit : {source}"),
        };
    }
}
