using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;

namespace GModForensic.Scanners.Development;

/// <summary>
/// Module factice de l'ETAPE 3.
/// <para>
/// Il ne lit rien et ne detecte rien : il existe uniquement pour valider l'orchestrateur —
/// progression, annulation, isolation des pannes — avant que les vrais modules n'arrivent a
/// l'etape 5. Il sera supprime a ce moment-la.
/// </para>
/// </summary>
public sealed class DemoScanModule : IScanModule
{
    private readonly int _steps;
    private readonly TimeSpan _stepDuration;
    private readonly ModuleStatus _finalStatus;

    public DemoScanModule(
        string id,
        string displayName,
        ScanCategory category,
        int weight = 10,
        int steps = 20,
        TimeSpan? stepDuration = null,
        RequiredCapabilities requires = RequiredCapabilities.None,
        ModuleStatus finalStatus = ModuleStatus.Success)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        Weight = weight;
        Requires = requires;
        _steps = steps;
        _stepDuration = stepDuration ?? TimeSpan.FromMilliseconds(25);
        _finalStatus = finalStatus;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public ScanCategory Category { get; }

    public RequiredCapabilities Requires { get; }

    public int Weight { get; }

    public async Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var observations = new List<Observation>();

        for (var step = 1; step <= _steps; step++)
        {
            // Point d'annulation : chaque boucle interne d'un vrai module doit faire de meme.
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(_stepDuration, context.Clock, cancellationToken).ConfigureAwait(false);

            // Un module produit des FAITS, jamais un jugement : ces observations simulent la
            // forme de ce que produiront les modules reels a l'etape 5.
            if (step % 10 == 0)
            {
                observations.Add(new Observation
                {
                    ModuleId = Id,
                    Kind = ObservationKind.SystemFact,
                    Timestamp = context.Clock.GetUtcNow(),
                    Source = $"{DisplayName} (demonstration)",
                    Evidence = Evidence.FromText(
                        "Demo",
                        $"{Id}#{step}",
                        "Observation de demonstration — les modules reels arrivent a l'etape 5."),
                });
            }

            context.Progress.Report(new ModuleProgress(
                Id,
                (double)step / _steps,
                $"{DisplayName} — element {step}/{_steps}",
                step));
        }

        context.Logger.Debug(Id, $"{_steps} elements simules");

        return new ModuleResult
        {
            ModuleId = Id,
            Status = _finalStatus,
            Observations = observations,
            ItemsExamined = _steps,
            StatusReason = _finalStatus == ModuleStatus.Success ? null : "Module de demonstration.",
        };
    }
}
