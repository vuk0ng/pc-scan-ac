using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Scanners.Development;

namespace GModForensic.Scanners;

/// <summary>
/// Inventaire des modules disponibles.
/// <para>
/// A l'etape 3, il ne contient que des modules de demonstration : ils permettent de valider
/// l'orchestrateur et l'interface avant l'implementation reelle (etape 5). Chaque entree sera
/// remplacee par son module reel, sans changement pour l'orchestrateur ni pour l'interface.
/// </para>
/// </summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IScanModule> CreateDemoModules() =>
    [
        new DemoScanModule("system", "Informations systeme", ScanCategory.System, weight: 2, steps: 6),
        new DemoScanModule("registry", "Registre — programmes utilises", ScanCategory.Registry, weight: 5, steps: 20),
        new DemoScanModule("bam", "BAM — dernieres executions", ScanCategory.Registry, weight: 3, steps: 10,
            requires: RequiredCapabilities.Administrator),
        new DemoScanModule("archives", "Historique d'archives", ScanCategory.Archives, weight: 2, steps: 8),
        new DemoScanModule("credentials", "Identifiants Windows (OINK)", ScanCategory.Credentials, weight: 2, steps: 5,
            requires: RequiredCapabilities.UserCredentialVault),
        new DemoScanModule("process", "Processus en cours", ScanCategory.Processes, weight: 8, steps: 30),
        new DemoScanModule("prefetch", "Prefetch", ScanCategory.Prefetch, weight: 10, steps: 40,
            requires: RequiredCapabilities.PrefetchFolder),
        new DemoScanModule("recent", "Fichiers recents", ScanCategory.RecentFiles, weight: 6, steps: 25),
        new DemoScanModule("usb", "Peripheriques USB", ScanCategory.RemovableDevices, weight: 5, steps: 15),
        new DemoScanModule("eventlog", "Journaux d'evenements", ScanCategory.EventLog, weight: 12, steps: 35,
            requires: RequiredCapabilities.SecurityEventLog),
        new DemoScanModule("usn", "Journal USN", ScanCategory.UsnJournal, weight: 25, steps: 60,
            requires: RequiredCapabilities.NtfsVolume | RequiredCapabilities.Administrator),
        new DemoScanModule("filesystem", "Systeme de fichiers", ScanCategory.FileSystem, weight: 20, steps: 50),
        new DemoScanModule("memory", "Analyse memoire passive", ScanCategory.Memory, weight: 30, steps: 45,
            requires: RequiredCapabilities.ProcessMemory),
    ];
}
