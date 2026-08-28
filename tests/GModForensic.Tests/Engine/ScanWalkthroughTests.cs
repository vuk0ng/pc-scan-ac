using System.Globalization;
using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;
using Xunit;
using Xunit.Abstractions;

namespace GModForensic.Tests.Engine;

/// <summary>
/// Deroule un scan complet de bout en bout et restitue ce que verra le staff :
/// barre de progression, etape courante, etat par module et journal d'execution.
/// <para>
/// Lancer avec : <c>dotnet test --filter Walkthrough --logger "console;verbosity=detailed"</c>
/// </para>
/// </summary>
public sealed class ScanWalkthroughTests
{
    private readonly ITestOutputHelper _output;

    public ScanWalkthroughTests(ITestOutputHelper output) => _output = output;

    private static Capabilities PartialCapabilities { get; } = new()
    {
        IsElevated = true,
        HasDebugPrivilege = true,
        // Le journal Security est inaccessible : un module doit etre ignore avec son motif.
        HasSecurityPrivilege = false,
        HasNtfsVolume = true,
        PrefetchFolderReadable = true,
        CanReadProcessMemory = true,
        UserCredentialVaultAccessible = true,
        Notes = ["Compte : STAFF\\controle", "Volumes NTFS : C:"],
    };

    [Fact]
    public async Task Walkthrough_un_scan_progresse_puis_s_annule_proprement()
    {
        var logger = new InMemoryScanLogger();

        var orchestrator = new ScanOrchestrator(
        [
            FakeModule.Progressing("registre", steps: 6, TimeSpan.FromMilliseconds(10), weight: 4),
            FakeModule.Progressing("prefetch", steps: 8, TimeSpan.FromMilliseconds(10), weight: 8),
            new FakeModule("eventlog", (_, _) => throw new InvalidOperationException("ne doit pas s'executer"),
                weight: 10, requires: RequiredCapabilities.SecurityEventLog),
            new FakeModule("usn", (_, _) => throw new UnauthorizedAccessException("volume verrouille"), weight: 20),
            new FakeModule("memoire", async (context, ct) =>
            {
                for (var i = 1; i <= 200; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(TimeSpan.FromMilliseconds(20), ct).ConfigureAwait(false);
                    context.Progress.Report(new ModuleProgress("memoire", i / 200d, $"region {i}/200", i));
                }

                return new ModuleResult { ModuleId = "memoire", Status = ModuleStatus.Success };
            }, weight: 30),
        ])
        { ProgressThrottle = TimeSpan.Zero };

        var context = new ScanContext
        {
            ScanId = "walkthrough",
            Configuration = new ScanConfiguration { ConsentGiven = true, MaxParallelModules = 4 },
            Capabilities = PartialCapabilities,
            Logger = logger,
            FileFacts = new FileFactsCache(new NullFileFactsProvider()),
            Clock = TimeProvider.System,
            Progress = new Progress<ModuleProgress>(),
        };

        using var cancellation = new CancellationTokenSource();
        ScanProgressSnapshot? latest = null;

        var progress = new Progress<ScanProgressSnapshot>(snapshot =>
        {
            latest = snapshot;

            // Le staff annule en cours de route, comme il pourra le faire depuis l'interface.
            // On attend qu'un module ait abouti pour verifier qu'une annulation preserve
            // bien ce qui est deja acquis.
            var somethingCompleted = snapshot.Modules.Any(m => m.Status == ModuleStatus.Success);

            if (somethingCompleted && !cancellation.IsCancellationRequested)
            {
                cancellation.Cancel();
            }
        });

        var result = await orchestrator.RunAsync(context, progress, cancellation.Token);

        _output.WriteLine("SCANNER FORENSIC GMod");
        _output.WriteLine(string.Empty);
        _output.WriteLine(RenderBar(latest!.OverallFraction));
        _output.WriteLine($"Etape courante   : {latest.CurrentStep}");
        _output.WriteLine($"Temps ecoule     : {result.Elapsed:mm\\:ss\\.ff}");
        _output.WriteLine($"Elements analyses: {result.ItemsExamined}");
        _output.WriteLine($"Scan annule      : {(result.WasCancelled ? "OUI — resultats partiels conserves" : "non")}");
        _output.WriteLine(string.Empty);

        foreach (var module in result.ModuleResults)
        {
            var reason = module.StatusReason is null ? string.Empty : $"  — {module.StatusReason}";
            _output.WriteLine($"  {module.StatusSymbol}  {module.ModuleId,-12}{module.ItemsExamined,5} elements{reason}");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine("Journal d'execution :");

        foreach (var entry in result.Log)
        {
            _output.WriteLine("  " + entry);
        }

        // Le scan s'est bien interrompu, sans exception et sans perdre ce qui etait acquis.
        Assert.True(result.WasCancelled);
        Assert.Equal(5, result.ModuleResults.Count);

        // Chaque etat de premiere classe du §25 est represente.
        Assert.Contains(result.ModuleResults, r => r.Status == ModuleStatus.Success);
        Assert.Contains(result.ModuleResults, r => r.Status == ModuleStatus.Failed);
        Assert.Contains(result.ModuleResults, r => r.Status == ModuleStatus.Skipped);
        Assert.Contains(result.ModuleResults, r => r.Status == ModuleStatus.Cancelled);

        // Le module ignore l'est pour un motif affichable, pas silencieusement.
        var skipped = result.ModuleResults.Single(r => r.ModuleId == "eventlog");
        Assert.Contains("Security", skipped.StatusReason, StringComparison.OrdinalIgnoreCase);

        // Le module en echec n'a pas empeche les autres de finir.
        Assert.Equal(ModuleStatus.Failed, result.ModuleResults.Single(r => r.ModuleId == "usn").Status);
        Assert.NotEmpty(result.Log);

        // Le travail effectue avant l'annulation n'est pas perdu.
        Assert.True(result.ItemsExamined > 0, "Les compteurs des modules interrompus ont ete perdus.");
    }

    private static string RenderBar(double fraction)
    {
        const int Width = 22;
        var filled = (int)Math.Round(fraction * Width);

        return new string('#', filled)
            + new string('.', Width - filled)
            + " " + (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
    }
}
