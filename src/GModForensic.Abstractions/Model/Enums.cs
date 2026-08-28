namespace GModForensic.Abstractions.Model;

/// <summary>Niveau de gravite d'une detection. Jamais une affirmation de culpabilite.</summary>
public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Degre de certitude sur le fait observe lui-meme (distinct de sa gravite).</summary>
public enum Confidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Etat d'execution d'un module. <see cref="Partial"/> est un etat de premiere classe :
/// « 9 processus lus sur 12, 3 acces refuses » est une information utile au staff,
/// pas une erreur a masquer (§25).
/// </summary>
public enum ModuleStatus
{
    NotStarted,
    Running,
    /// <summary>✓ Verification reussie.</summary>
    Success,
    /// <summary>⚠ Verification partielle.</summary>
    Partial,
    /// <summary>✕ Verification impossible.</summary>
    Failed,
    /// <summary>Module non execute : capacite requise absente, ou desactive par le staff.</summary>
    Skipped,
    Cancelled,
}

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Sections du rapport final (§23).</summary>
public enum ScanCategory
{
    System,
    Processes,
    Memory,
    FileSystem,
    UsnJournal,
    Prefetch,
    Registry,
    RecentFiles,
    Archives,
    RemovableDevices,
    EventLog,
    Downloads,
    Discord,
    Credentials,
    GMod,
    AntiForensic,
}

/// <summary>
/// Nature d'un fait collecte. Volontairement descriptive et neutre : un module
/// n'exprime jamais de jugement, il decrit ce qu'il a lu (voir <see cref="Observation"/>).
/// </summary>
public enum ObservationKind
{
    SystemFact,

    ProcessRunning,
    ProcessModuleLoaded,
    ProcessNetworkEndpoint,
    ProcessStartTime,

    MemoryString,

    FileCreated,
    FileDeleted,
    FileRenamed,
    FileModified,
    FileMetadata,

    ExecutionRecord,
    RegistryValue,
    RecentDocument,
    ArchiveHistoryEntry,

    DevicePresent,
    DeviceHistory,

    EventLogRecord,
    DownloadOrigin,
    CredentialEntry,

    /// <summary>Un artefact attendu est absent (dossier Prefetch vide, journal recree...). Voir M18.</summary>
    ArtifactMissing,
}

/// <summary>Resultat de la verification de signature Authenticode d'un fichier.</summary>
public enum SignatureStatus
{
    /// <summary>Non evalue (fichier absent, trop volumineux, ou verification non demandee).</summary>
    Unknown,
    NotSigned,
    Valid,
    Invalid,
    Expired,
    Untrusted,
}
