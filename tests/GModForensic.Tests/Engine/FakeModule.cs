using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;

namespace GModForensic.Tests.Engine;

/// <summary>Module pilotable, utilise pour eprouver l'orchestrateur sans dependre de Windows.</summary>
internal sealed class FakeModule : IScanModule
{
    private readonly Func<ScanContext, CancellationToken, Task<ModuleResult>> _body;

    public FakeModule(
        string id,
        Func<ScanContext, CancellationToken, Task<ModuleResult>> body,
        int weight = 10,
        RequiredCapabilities requires = RequiredCapabilities.None)
    {
        Id = id;
        _body = body;
        Weight = weight;
        Requires = requires;
    }

    public string Id { get; }

    public string DisplayName => $"Module {Id}";

    public ScanCategory Category => ScanCategory.System;

    public RequiredCapabilities Requires { get; }

    public int Weight { get; }

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken) =>
        _body(context, cancellationToken);

    /// <summary>Module qui termine immediatement avec succes.</summary>
    public static FakeModule Succeeding(string id, int items = 5, int weight = 10) =>
        new(id, (_, _) => Task.FromResult(new ModuleResult
        {
            ModuleId = id,
            Status = ModuleStatus.Success,
            ItemsExamined = items,
        }), weight);

    /// <summary>Module qui signale sa progression puis reussit.</summary>
    public static FakeModule Progressing(string id, int steps, TimeSpan stepDelay, int weight = 10) =>
        new(id, async (context, ct) =>
        {
            for (var i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(stepDelay, ct).ConfigureAwait(false);
                context.Progress.Report(new ModuleProgress(id, (double)i / steps, $"etape {i}", i));
            }

            return new ModuleResult { ModuleId = id, Status = ModuleStatus.Success, ItemsExamined = steps };
        }, weight);
}
