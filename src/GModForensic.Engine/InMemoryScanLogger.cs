using System.Collections.Concurrent;
using GModForensic.Abstractions.Logging;

namespace GModForensic.Engine;

/// <summary>
/// Journal d'execution et journal d'acces, tenus en memoire puis ecrits dans le rapport.
/// Aucun contenu sensible n'est journalise : ni contenu de fichier, ni valeur de credential.
/// </summary>
public sealed class InMemoryScanLogger : IScanLogger
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly ConcurrentQueue<AccessEntry> _accesses = new();
    private readonly TimeProvider _clock;

    public InMemoryScanLogger(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public LogLevel MinimumLevel { get; init; } = LogLevel.Debug;

    /// <summary>Notifie chaque ligne au fur et a mesure, pour l'affichage temps reel.</summary>
    public event Action<LogEntry>? EntryWritten;

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public IReadOnlyList<AccessEntry> Accesses => _accesses.ToArray();

    public void Log(LogLevel level, string moduleId, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry
        {
            Timestamp = _clock.GetUtcNow(),
            Level = level,
            ModuleId = moduleId,
            Message = message,
        };

        _entries.Enqueue(entry);
        EntryWritten?.Invoke(entry);
    }

    public void RecordAccess(string moduleId, string resourceKind, string resource) =>
        _accesses.Enqueue(new AccessEntry
        {
            Timestamp = _clock.GetUtcNow(),
            ModuleId = moduleId,
            ResourceKind = resourceKind,
            Resource = resource,
        });
}
