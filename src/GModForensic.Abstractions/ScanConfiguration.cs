namespace GModForensic.Abstractions;

/// <summary>Profils de scan proposes au staff (§26).</summary>
public enum ScanProfile
{
    /// <summary>Registre, Prefetch et processus. Retour en moins d'une minute.</summary>
    Quick,
    Standard,
    /// <summary>Ajoute l'analyse memoire et le journal USN complet.</summary>
    Deep,
}

/// <summary>
/// Configuration d'un scan. Surchargeable par <c>%ProgramData%\GModForensicScanner\config.json</c>
/// puis par l'ecran d'accueil.
/// </summary>
public sealed record ScanConfiguration
{
    public ScanProfile Profile { get; init; } = ScanProfile.Standard;

    /// <summary>Delai au-dela duquel un module est interrompu, ses resultats partiels conserves.</summary>
    public TimeSpan ModuleTimeout { get; init; } = TimeSpan.FromSeconds(180);

    /// <summary>La charge est I/O disque : au-dela de 4, le parallelisme degrade sur disque mecanique.</summary>
    public int MaxParallelModules { get; init; } = 4;

    /// <summary>Au-dela de cette taille, seules les metadonnees sont lues (pas de SHA-256).</summary>
    public long MaxFileSizeForHashBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Racines analysees par le module systeme de fichiers. Jamais le disque entier (§26).</summary>
    public IReadOnlyList<string> FileSystemRoots { get; init; } =
    [
        "%USERPROFILE%\\Downloads",
        "%USERPROFILE%\\Desktop",
        "%USERPROFILE%\\Documents",
        "%LOCALAPPDATA%\\Temp",
        "%APPDATA%",
    ];

    public IReadOnlyList<string> FileSystemExtensions { get; init; } =
        [".exe", ".dll", ".bat", ".cmd", ".ps1", ".jar", ".lua", ".gma", ".zip", ".rar", ".7z"];

    public int FileSystemMaxDepth { get; init; } = 6;

    /// <summary>Identifiants des modules explicitement desactives par le staff.</summary>
    public IReadOnlySet<string> DisabledModuleIds { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nom du membre du staff realisant le controle, inscrit au rapport.</summary>
    public string? OperatorName { get; init; }

    /// <summary>Identifiant de la personne analysee, inscrit au rapport.</summary>
    public string? SubjectIdentifier { get; init; }

    /// <summary>Consentement explicite recueilli avant le scan. Requis pour demarrer.</summary>
    public bool ConsentGiven { get; init; }
}
