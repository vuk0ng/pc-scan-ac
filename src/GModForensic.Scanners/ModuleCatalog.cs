using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Scanners.Development;

namespace GModForensic.Scanners;

/// <summary>
/// Inventaire des modules.
/// <para>
/// Etape 5 en cours : les modules reels remplacent progressivement ceux de demonstration.
/// Ces derniers ne lisent aucun artefact — leur libelle le dit, pour qu'un rapport ne puisse
/// jamais laisser croire a une verification qui n'a pas eu lieu.
/// </para>
/// </summary>
public static class ModuleCatalog
{
    /// <summary>Modules reellement implementes.</summary>
    public static IReadOnlyList<IScanModule> CreateRealModules() =>
    [
        new SystemInfoScanner(),
        new RegistryExecutionScanner(),
        new BamScanner(),
        new ArchiveHistoryScanner(),
        new CredentialTraceScanner(),
    ];

    /// <summary>
    /// Modules pas encore implementes, representes par un module de demonstration.
    /// Chacun disparait des que le module reel correspondant arrive.
    /// </summary>
    public static IReadOnlyList<IScanModule> CreatePendingModules() =>
    [
        new DemoScanModule("process", "Processus en cours (demonstration)", ScanCategory.Processes, weight: 8, steps: 20),
        new DemoScanModule("prefetch", "Prefetch (demonstration)", ScanCategory.Prefetch, weight: 10, steps: 25,
            requires: RequiredCapabilities.PrefetchFolder),
        new DemoScanModule("recent", "Fichiers recents (demonstration)", ScanCategory.RecentFiles, weight: 6, steps: 15),
        new DemoScanModule("usb", "Peripheriques USB (demonstration)", ScanCategory.RemovableDevices, weight: 5, steps: 12),
        new DemoScanModule("eventlog", "Journaux d\'evenements (demonstration)", ScanCategory.EventLog, weight: 12, steps: 20,
            requires: RequiredCapabilities.SecurityEventLog),
        new DemoScanModule("usn", "Journal USN (demonstration)", ScanCategory.UsnJournal, weight: 25, steps: 30,
            requires: RequiredCapabilities.NtfsVolume | RequiredCapabilities.Administrator),
        new DemoScanModule("filesystem", "Systeme de fichiers (demonstration)", ScanCategory.FileSystem, weight: 20, steps: 25),
        new DemoScanModule("memory", "Analyse memoire passive (demonstration)", ScanCategory.Memory, weight: 30, steps: 25,
            requires: RequiredCapabilities.ProcessMemory),
    ];

    public static IReadOnlyList<IScanModule> CreateAll() =>
        [.. CreateRealModules(), .. CreatePendingModules()];
}
