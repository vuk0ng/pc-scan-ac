using GModForensic.Abstractions;
using GModForensic.Abstractions.Logging;
using GModForensic.Abstractions.Model;

namespace GModForensic.Engine;

/// <summary>
/// Enveloppe d'execution d'un module.
/// <para>
/// C'est ici qu'est tenue la promesse du §25 : « le programme ne doit jamais planter parce
/// qu'une cle de registre, un processus ou un fichier est inaccessible ». Un module qui leve
/// une exception, depasse son delai ou reclame une capacite absente produit un
/// <see cref="ModuleResult"/> qui decrit ce qui s'est passe — et le scan continue.
/// </para>
/// </summary>
internal static class ModuleHost
{
    public static async Task<ModuleResult> RunAsync(
        IScanModule module,
        ScanContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ModuleResult
            {
                ModuleId = module.Id,
                Status = ModuleStatus.Cancelled,
                StatusReason = "Scan annule avant le demarrage de ce module.",
            };
        }

        if (context.Configuration.DisabledModuleIds.Contains(module.Id))
        {
            return ModuleResult.Skipped(module.Id, "Desactive par le staff.");
        }

        var missing = context.Capabilities.ExplainMissing(module.Requires);
        if (missing is not null)
        {
            context.Logger.Warn(module.Id, $"○ Ignore : {missing}");
            return ModuleResult.Skipped(module.Id, missing);
        }

        var startedAt = context.Clock.GetTimestamp();

        using var timeoutSource = new CancellationTokenSource(timeout, context.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            var result = await module.RunAsync(context, linked.Token).ConfigureAwait(false);
            return result with { Elapsed = context.Clock.GetElapsedTime(startedAt) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Logger.Info(module.Id, "⊘ Interrompu par l'annulation du scan.");
            return new ModuleResult
            {
                ModuleId = module.Id,
                Status = ModuleStatus.Cancelled,
                StatusReason = "Scan annule.",
                Elapsed = context.Clock.GetElapsedTime(startedAt),
            };
        }
        catch (OperationCanceledException)
        {
            // Delai depasse : ce n'est pas un echec, les resultats deja obtenus restent valides.
            var reason = $"Delai depasse ({timeout.TotalSeconds:0} s).";
            context.Logger.Warn(module.Id, $"⚠ {reason}");
            return new ModuleResult
            {
                ModuleId = module.Id,
                Status = ModuleStatus.Partial,
                StatusReason = reason,
                Elapsed = context.Clock.GetElapsedTime(startedAt),
            };
        }
        catch (Exception ex)
        {
            // Dernier filet. Un module bien ecrit gere ses propres erreurs et renvoie Partial ;
            // arriver ici signale un defaut du module, jamais une raison d'arreter le scan.
            var reason = $"{ex.GetType().Name} : {ex.Message}";
            context.Logger.Error(module.Id, $"✕ Erreur inattendue — {reason}");
            return new ModuleResult
            {
                ModuleId = module.Id,
                Status = ModuleStatus.Failed,
                StatusReason = reason,
                Elapsed = context.Clock.GetElapsedTime(startedAt),
                Diagnostics = [Diagnostic.Error(module.Id, reason)],
            };
        }
    }
}
