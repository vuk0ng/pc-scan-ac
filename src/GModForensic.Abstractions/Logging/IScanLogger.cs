namespace GModForensic.Abstractions.Logging;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
}

public sealed record LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string ModuleId { get; init; }
    public required string Message { get; init; }

    public override string ToString() =>
        $"[{Timestamp.LocalDateTime:HH:mm:ss}] [{ModuleId}] {Message}";
}

/// <summary>
/// Ressource effectivement lue par le programme.
/// <para>
/// Le journal d'acces est l'exigence de transparence du §1 : n'importe qui, staff comme
/// personne analysee, peut verifier a posteriori exactement ce que le programme a touche.
/// </para>
/// </summary>
public sealed record AccessEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string ModuleId { get; init; }
    /// <summary>« RegistryKey », « File », « Process », « Volume », « EventLogChannel ».</summary>
    public required string ResourceKind { get; init; }
    public required string Resource { get; init; }
}

public interface IScanLogger
{
    void Log(LogLevel level, string moduleId, string message);

    /// <summary>Enregistre une ressource lue, pour le journal d'acces du rapport.</summary>
    void RecordAccess(string moduleId, string resourceKind, string resource);
}

public static class ScanLoggerExtensions
{
    public static void Info(this IScanLogger logger, string moduleId, string message) =>
        logger.Log(LogLevel.Info, moduleId, message);

    public static void Warn(this IScanLogger logger, string moduleId, string message) =>
        logger.Log(LogLevel.Warn, moduleId, message);

    public static void Error(this IScanLogger logger, string moduleId, string message) =>
        logger.Log(LogLevel.Error, moduleId, message);

    public static void Debug(this IScanLogger logger, string moduleId, string message) =>
        logger.Log(LogLevel.Debug, moduleId, message);
}
