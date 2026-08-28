using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>
/// Execute les modules, agrege leur progression et garantit qu'un scan se termine toujours
/// par un resultat exploitable — y compris apres une annulation.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly IReadOnlyList<IScanModule> _modules;

    public ScanOrchestrator(IEnumerable<IScanModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        // Les modules legers passent en premier : le staff voit des resultats immediatement.
        _modules = modules.OrderBy(m => m.Weight).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Cadence minimale entre deux notifications de progression. L'interface reste fluide
    /// meme si un module signale son avancement des milliers de fois par seconde.
    /// Mise a <see cref="TimeSpan.Zero"/> dans les tests.
    /// </summary>
    public TimeSpan ProgressThrottle { get; init; } = TimeSpan.FromMilliseconds(40);

    public IReadOnlyList<IScanModule> Modules => _modules;

    public async Task<ScanRunResult> RunAsync(
        ScanContext context,
        IProgress<ScanProgressSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var clock = context.Clock;
        var startedUtc = clock.GetUtcNow();
        var startTimestamp = clock.GetTimestamp();

        var tracker = new ProgressTracker(_modules, clock, ProgressThrottle, startTimestamp, progress);
        var results = new ModuleResult[_modules.Count];

        context.Logger.Info("scan",
            $"Scan demarre — profil {context.Configuration.Profile} — {_modules.Count} modules");

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, context.Configuration.MaxParallelModules),
            // Le jeton n'est PAS passe ici : l'annulation doit produire des resultats
            // partiels exploitables, pas faire remonter une exception qui les perdrait.
        };

        var indexed = _modules.Select((module, index) => (module, index)).ToArray();

        await Parallel.ForEachAsync(indexed, options, async (item, _) =>
        {
            var (module, index) = item;

            tracker.SetStatus(module.Id, ModuleStatus.Running);
            context.Logger.Debug(module.Id, $"⟳ {module.DisplayName}");

            var moduleContext = context with
            {
                Progress = new Progress<ModuleProgress>(tracker.Report),
            };

            var result = await ModuleHost
                .RunAsync(module, moduleContext, context.Configuration.ModuleTimeout, cancellationToken)
                .ConfigureAwait(false);

            // Un module interrompu ou en echec n'a pas pu renseigner son compteur : on reprend
            // le dernier avancement qu'il avait signale, pour que le travail deja effectue
            // apparaisse dans le rapport plutot que de disparaitre.
            if (result.ItemsExamined == 0)
            {
                result = result with { ItemsExamined = tracker.LastItemsExamined(module.Id) };
            }

            results[index] = result;
            tracker.Complete(result);

            context.Logger.Log(
                result.Status is ModuleStatus.Failed ? LogLevel.Error : LogLevel.Info,
                module.Id,
                $"{result.StatusSymbol} {module.DisplayName} — {Describe(result)}");
        }).ConfigureAwait(false);

        var elapsed = clock.GetElapsedTime(startTimestamp);
        var wasCancelled = cancellationToken.IsCancellationRequested;

        tracker.Finish();

        var observationCount = results.Sum(r => r.Observations.Count);
        context.Logger.Info("scan", wasCancelled
            ? $"Scan interrompu apres {elapsed.TotalSeconds:0.0} s — {observationCount} observations conservees"
            : $"Scan termine en {elapsed.TotalSeconds:0.0} s — {results.Sum(r => r.ItemsExamined)} elements — {observationCount} observations");

        var logger = context.Logger as InMemoryScanLogger;

        return new ScanRunResult
        {
            ScanId = context.ScanId,
            StartedUtc = startedUtc,
            Elapsed = elapsed,
            WasCancelled = wasCancelled,
            ModuleResults = results,
            Log = logger?.Entries ?? [],
            AccessLog = logger?.Accesses ?? [],
        };
    }

    private static string Describe(ModuleResult result)
    {
        var elapsed = $"{result.Elapsed.TotalMilliseconds:0} ms";

        return result.Status switch
        {
            ModuleStatus.Skipped => result.StatusReason ?? "ignore",
            ModuleStatus.Cancelled => result.StatusReason ?? "annule",
            ModuleStatus.Failed => $"{result.StatusReason} ({elapsed})",
            _ when result.StatusReason is not null =>
                $"{result.ItemsExamined} elements, {result.Observations.Count} observations — {result.StatusReason} ({elapsed})",
            _ => $"{result.ItemsExamined} elements, {result.Observations.Count} observations ({elapsed})",
        };
    }
}
