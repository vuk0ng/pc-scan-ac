using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;
using Xunit;

namespace GModForensic.Tests.Engine;

public sealed class ScanOrchestratorTests
{
    private static ScanContext CreateContext(
        Capabilities? capabilities = null,
        ScanConfiguration? configuration = null)
    {
        var logger = new InMemoryScanLogger();

        return new ScanContext
        {
            ScanId = "test",
            Configuration = configuration ?? new ScanConfiguration
            {
                MaxParallelModules = 4,
                ModuleTimeout = TimeSpan.FromSeconds(30),
            },
            Capabilities = capabilities ?? AllCapabilities,
            Logger = logger,
            FileFacts = new FileFactsCache(new NullFileFactsProvider()),
            Clock = TimeProvider.System,
            Progress = new Progress<ModuleProgress>(),
        };
    }

    private static Capabilities AllCapabilities { get; } = new()
    {
        IsElevated = true,
        HasDebugPrivilege = true,
        HasSecurityPrivilege = true,
        HasNtfsVolume = true,
        PrefetchFolderReadable = true,
        CanReadProcessMemory = true,
        UserCredentialVaultAccessible = true,
    };

    [Fact]
    public async Task Un_scan_execute_tous_les_modules_et_atteint_cent_pour_cent()
    {
        var orchestrator = new ScanOrchestrator(
        [
            FakeModule.Succeeding("a", items: 3),
            FakeModule.Succeeding("b", items: 7),
            FakeModule.Succeeding("c", items: 11),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var snapshots = new List<ScanProgressSnapshot>();
        var progress = new Progress<ScanProgressSnapshot>(snapshots.Add);

        var result = await orchestrator.RunAsync(CreateContext(), progress, CancellationToken.None);

        Assert.False(result.WasCancelled);
        Assert.Equal(3, result.ModuleResults.Count);
        Assert.All(result.ModuleResults, r => Assert.Equal(ModuleStatus.Success, r.Status));
        Assert.Equal(21, result.ItemsExamined);

        // La derniere notification construite doit toujours annoncer 100 %, quelle que soit l'issue.
        Assert.NotEmpty(snapshots);
        Assert.Equal(1d, snapshots.MaxBy(s => s.Sequence)!.OverallFraction, precision: 6);
    }

    [Fact]
    public async Task La_progression_est_croissante_et_ponderee()
    {
        // Le module lourd pese 90 % de la barre : sa progression doit dominer.
        var orchestrator = new ScanOrchestrator(
        [
            FakeModule.Progressing("leger", steps: 4, TimeSpan.FromMilliseconds(5), weight: 1),
            FakeModule.Progressing("lourd", steps: 10, TimeSpan.FromMilliseconds(5), weight: 9),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var snapshots = new List<ScanProgressSnapshot>();
        var gate = new object();
        var progress = new Progress<ScanProgressSnapshot>(s =>
        {
            lock (gate)
            {
                snapshots.Add(s);
            }
        });

        await orchestrator.RunAsync(CreateContext(), progress, CancellationToken.None);

        Assert.NotEmpty(snapshots);

        // Les notifications peuvent etre DELIVREES dans le desordre : on les remet dans leur
        // ordre de construction, qui est celui que doit respecter un consommateur.
        var ordered = snapshots.OrderBy(s => s.Sequence).ToList();

        Assert.Equal(1d, ordered[^1].OverallFraction, precision: 6);
        Assert.Equal(ordered.Select(s => s.Sequence).Distinct().Count(), ordered.Count);

        // Aucune regression de la barre de progression.
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(
                ordered[i].OverallFraction >= ordered[i - 1].OverallFraction - 1e-9,
                $"La progression a recule : {ordered[i - 1].OverallFraction:F4} puis {ordered[i].OverallFraction:F4}");
        }
    }

    [Fact]
    public async Task Une_annulation_conserve_les_resultats_deja_obtenus()
    {
        using var cancellation = new CancellationTokenSource();

        var started = new TaskCompletionSource();

        var orchestrator = new ScanOrchestrator(
        [
            FakeModule.Succeeding("rapide", items: 4, weight: 1),
            new FakeModule("lent", async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return new ModuleResult { ModuleId = "lent", Status = ModuleStatus.Success };
            }, weight: 50),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var run = orchestrator.RunAsync(CreateContext(), progress: null, cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.WasCancelled);

        // Le module deja termine garde son resultat : un scan annule reste exportable.
        var fast = result.ModuleResults.Single(r => r.ModuleId == "rapide");
        Assert.Equal(ModuleStatus.Success, fast.Status);
        Assert.Equal(4, fast.ItemsExamined);

        var slow = result.ModuleResults.Single(r => r.ModuleId == "lent");
        Assert.Equal(ModuleStatus.Cancelled, slow.Status);
    }

    [Fact]
    public async Task Un_module_qui_leve_une_exception_n_interrompt_pas_le_scan()
    {
        var orchestrator = new ScanOrchestrator(
        [
            new FakeModule("fautif", (_, _) => throw new InvalidOperationException("cle absente")),
            FakeModule.Succeeding("sain", items: 6),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var result = await orchestrator.RunAsync(CreateContext(), progress: null, CancellationToken.None);

        var failed = result.ModuleResults.Single(r => r.ModuleId == "fautif");
        Assert.Equal(ModuleStatus.Failed, failed.Status);
        Assert.Contains("cle absente", failed.StatusReason);
        Assert.Equal("✕", failed.StatusSymbol);

        // Exigence §25 : le scan continue toujours.
        Assert.Equal(ModuleStatus.Success, result.ModuleResults.Single(r => r.ModuleId == "sain").Status);
    }

    [Fact]
    public async Task Un_module_sans_la_capacite_requise_est_ignore_avec_son_motif()
    {
        var limited = AllCapabilities with { IsElevated = false, CanReadProcessMemory = false };

        var orchestrator = new ScanOrchestrator(
        [
            new FakeModule("usn", (_, _) => throw new InvalidOperationException("ne doit jamais s'executer"),
                requires: RequiredCapabilities.Administrator),
            FakeModule.Succeeding("registre"),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var result = await orchestrator.RunAsync(
            CreateContext(limited), progress: null, CancellationToken.None);

        var skipped = result.ModuleResults.Single(r => r.ModuleId == "usn");
        Assert.Equal(ModuleStatus.Skipped, skipped.Status);
        Assert.Contains("administrateur", skipped.StatusReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_depassement_de_delai_donne_Partiel_et_non_Echec()
    {
        var configuration = new ScanConfiguration
        {
            ModuleTimeout = TimeSpan.FromMilliseconds(100),
            MaxParallelModules = 2,
        };

        var orchestrator = new ScanOrchestrator(
        [
            new FakeModule("interminable", async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return new ModuleResult { ModuleId = "interminable", Status = ModuleStatus.Success };
            }),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var result = await orchestrator.RunAsync(
            CreateContext(configuration: configuration), progress: null, CancellationToken.None);

        var timed = result.ModuleResults.Single();
        Assert.Equal(ModuleStatus.Partial, timed.Status);
        Assert.Contains("Delai depasse", timed.StatusReason);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task Un_module_desactive_par_le_staff_n_est_pas_execute()
    {
        var configuration = new ScanConfiguration
        {
            DisabledModuleIds = new HashSet<string>(["memoire"], StringComparer.OrdinalIgnoreCase),
        };

        var orchestrator = new ScanOrchestrator(
        [
            new FakeModule("memoire", (_, _) => throw new InvalidOperationException("ne doit jamais s'executer")),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var result = await orchestrator.RunAsync(
            CreateContext(configuration: configuration), progress: null, CancellationToken.None);

        Assert.Equal(ModuleStatus.Skipped, result.ModuleResults.Single().Status);
    }
}
