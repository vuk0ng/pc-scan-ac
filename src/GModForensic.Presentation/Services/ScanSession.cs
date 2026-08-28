using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;

namespace GModForensic.Presentation.Services;

/// <summary>Assemble l'orchestrateur, les modules et les capacites pour l'interface.</summary>
public sealed class ScanSession : IScanSession
{
    private readonly IReadOnlyList<IScanModule> _modules;
    private readonly IFileFactsProvider _fileFacts;
    private readonly TimeProvider _clock;

    public ScanSession(
        IScanModuleProvider moduleProvider,
        ICapabilityProvider capabilityProvider,
        IFileFactsProvider fileFacts,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(moduleProvider);
        ArgumentNullException.ThrowIfNull(capabilityProvider);

        _modules = moduleProvider.CreateModules();
        _fileFacts = fileFacts;
        _clock = clock ?? TimeProvider.System;
        Capabilities = capabilityProvider.Measure();
    }

    public Capabilities Capabilities { get; }

    public IReadOnlyList<IScanModule> Modules => _modules;

    /// <summary>Journal du dernier scan, repris par l'ecran de resultats et l'export.</summary>
    public InMemoryScanLogger? LastLogger { get; private set; }

    public async Task<ScanRunResult> RunAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgressSnapshot> progress,
        CancellationToken cancellationToken)
    {
        var logger = new InMemoryScanLogger(_clock);
        LastLogger = logger;

        var orchestrator = new ScanOrchestrator(_modules);

        var context = new ScanContext
        {
            ScanId = Guid.NewGuid().ToString("n"),
            Configuration = configuration,
            Capabilities = Capabilities,
            Logger = logger,
            FileFacts = new FileFactsCache(_fileFacts),
            Clock = _clock,
            Progress = new Progress<ModuleProgress>(),
        };

        return await orchestrator.RunAsync(context, progress, cancellationToken).ConfigureAwait(false);
    }
}
