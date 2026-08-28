using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;
using GModForensic.Presentation.Services;
using GModForensic.Tests.Engine;

namespace GModForensic.Tests.Presentation;

/// <summary>Session pilotable, pour eprouver les ViewModels sans dependre des modules Windows.</summary>
internal sealed class FakeScanSession : IScanSession
{
    private readonly ScanOrchestrator _orchestrator;

    public FakeScanSession(Capabilities? capabilities = null, IEnumerable<IScanModule>? modules = null)
    {
        Capabilities = capabilities ?? Everything;

        Modules = modules?.ToArray() ??
        [
            FakeModule.Succeeding("registre", items: 12, weight: 3, observations: 2),
            FakeModule.Succeeding("prefetch", items: 40, weight: 8, observations: 3),
            new FakeModule("usn", (_, _) => Task.FromResult(new ModuleResult
            {
                ModuleId = "usn",
                Status = ModuleStatus.Success,
                ItemsExamined = 900,
                Observations = [FakeModule.Fact("usn", 0)],
            }), weight: 20, requires: RequiredCapabilities.Administrator),
        ];

        _orchestrator = new ScanOrchestrator(Modules) { ProgressThrottle = TimeSpan.Zero };
    }

    public static Capabilities Everything { get; } = new()
    {
        IsElevated = true,
        HasDebugPrivilege = true,
        HasSecurityPrivilege = true,
        HasNtfsVolume = true,
        PrefetchFolderReadable = true,
        CanReadProcessMemory = true,
        UserCredentialVaultAccessible = true,
        Notes = ["Compte : STAFF\\controle"],
    };

    public Capabilities Capabilities { get; }

    public IReadOnlyList<IScanModule> Modules { get; }

    public ScanConfiguration? LastConfiguration { get; private set; }

    public Task<ScanRunResult> RunAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgressSnapshot> progress,
        CancellationToken cancellationToken)
    {
        LastConfiguration = configuration;

        var context = new ScanContext
        {
            ScanId = "fake",
            Configuration = configuration,
            Capabilities = Capabilities,
            Logger = new InMemoryScanLogger(),
            FileFacts = new FileFactsCache(new NullFileFactsProvider()),
            Clock = TimeProvider.System,
            Progress = new Progress<ModuleProgress>(),
        };

        return _orchestrator.RunAsync(context, progress, cancellationToken);
    }
}

internal sealed class RecordingExporter : IReportExporter
{
    public ExportRequest? LastRequest { get; private set; }

    public bool ShouldFail { get; set; }

    public IReadOnlyList<string> Export(ExportRequest request)
    {
        LastRequest = request;

        if (ShouldFail)
        {
            throw new IOException("disque plein");
        }

        var written = new List<string>();

        if (request.IncludeJson)
        {
            written.Add(Path.Combine(request.OutputDirectory, "rapport.json"));
        }

        if (request.IncludeText)
        {
            written.Add(Path.Combine(request.OutputDirectory, "resume.txt"));
        }

        return written;
    }
}
