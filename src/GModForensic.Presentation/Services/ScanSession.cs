using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Engine;

namespace GModForensic.Presentation.Services;

/// <summary>Assemble l'orchestrateur, les modules et les capacites pour l'interface.</summary>
public sealed class ScanSession : IScanSession
{
    private readonly IReadOnlyList<IScanModule> _modules;
    private readonly ICapabilityProvider _capabilityProvider;
    private readonly IFileFactsProvider _fileFacts;
    private readonly TimeProvider _clock;

    public ScanSession(
        IScanModuleProvider moduleProvider,
        ICapabilityProvider capabilityProvider,
        IFileFactsProvider fileFacts,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(moduleProvider);

        _modules = moduleProvider.CreateModules();
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _fileFacts = fileFacts;
        _clock = clock ?? TimeProvider.System;
    }

    public Capabilities? Capabilities { get; private set; }

    /// <summary>Delai au-dela duquel la mesure est abandonnee et rapportee comme partielle.</summary>
    public TimeSpan MeasurementTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<Capabilities> MeasureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        if (Capabilities is { } already)
        {
            return already;
        }

        using var timeout = new CancellationTokenSource(MeasurementTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Capabilities measured;

        try
        {
            // Task.Run : la mesure ne doit jamais s'executer sur le fil d'interface.
            measured = await Task.Run(_capabilityProvider.Measure, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Un sondage qui n'aboutit pas ne doit pas bloquer le staff : on repart en mode
            // degrade, en disant clairement ce qui s'est passe.
            measured = Capabilities.None with
            {
                Notes = ["La mesure des privileges n'a pas abouti dans le delai imparti. "
                         + "Les verifications qui en dependent seront ignorees."],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            measured = Capabilities.None with
            {
                Notes = [$"La mesure des privileges a echoue : {ex.Message}"],
            };
        }

        Capabilities = measured;
        return measured;
    }

    public IReadOnlyList<IScanModule> Modules => _modules;

    /// <summary>Journal du dernier scan, repris par l'ecran de resultats et l'export.</summary>
    public InMemoryScanLogger? LastLogger { get; private set; }

    public async Task<ScanRunResult> RunAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgressSnapshot> progress,
        CancellationToken cancellationToken)
    {
        var capabilities = await MeasureCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        var logger = new InMemoryScanLogger(_clock);
        LastLogger = logger;

        var orchestrator = new ScanOrchestrator(_modules);

        var context = new ScanContext
        {
            ScanId = Guid.NewGuid().ToString("n"),
            Configuration = configuration,
            Capabilities = capabilities,
            Logger = logger,
            FileFacts = new FileFactsCache(_fileFacts),
            Clock = _clock,
            Progress = new Progress<ModuleProgress>(),
        };

        return await orchestrator.RunAsync(context, progress, cancellationToken).ConfigureAwait(false);
    }
}
