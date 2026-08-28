using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Native.Credentials;

namespace GModForensic.Scanners;

/// <summary>
/// M17 — traces dans le Gestionnaire d'identifiants Windows (§17).
/// <para>
/// Ce module detecte l'EXISTENCE d'une entree nommee, rien d'autre. Il ne lit aucun mot de
/// passe, n'en affiche aucun, n'en exporte aucun ; il ne contourne pas le Gestionnaire
/// d'identifiants et ne touche pas a la DPAPI.
/// </para>
/// <para>
/// Limite a afficher avec le resultat : <c>CredEnumerate</c> ne voit que le coffre de
/// l'utilisateur qui execute le programme. Lance sous un autre compte que celui du joueur,
/// il renverra « non » alors que l'entree existe — un « non » sans cette precision serait
/// trompeur.
/// </para>
/// </summary>
public sealed class CredentialTraceScanner : IScanModule
{
    /// <summary>Motifs recherches dans les NOMS d'entree, sans distinction de casse.</summary>
    private static readonly string[] Patterns = ["oink"];

    public string Id => "credentials";

    public string DisplayName => "Identifiants Windows (OINK)";

    public ScanCategory Category => ScanCategory.Credentials;

    public RequiredCapabilities Requires => RequiredCapabilities.UserCredentialVault;

    public int Weight => 2;

    public Task<ModuleResult> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ModuleRun(Id, context);
        run.Progress(0.2, "Lecture du coffre d'identifiants");

        context.Logger.RecordAccess(Id, "CredentialVault", "CredEnumerateW (noms uniquement)");

        IReadOnlyList<CredentialEntry> entries;

        try
        {
            entries = CredentialEnumerator.Enumerate();
        }
        catch (Exception ex)
        {
            run.Note(Diagnostic.Error("Gestionnaire d'identifiants", ex.Message));
            return Task.FromResult(run.ToResult("Coffre d'identifiants inaccessible."));
        }

        var matches = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Counted();

            var haystack = $"{entry.TargetName} {entry.UserName}";

            if (!Patterns.Any(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            matches++;

            run.Add(new Observation
            {
                ModuleId = Id,
                Kind = ObservationKind.CredentialEntry,
                Timestamp = entry.LastWritten,
                Fields = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["target"] = entry.TargetName,
                    ["user"] = entry.UserName ?? "(non renseigne)",
                    ["type"] = entry.Type,
                    // Formule volontairement explicite : elle figure telle quelle au rapport.
                    ["secret"] = "NON LU — le mot de passe n'est jamais accede par ce logiciel",
                    ["scope"] = "coffre de l'utilisateur executant le programme uniquement",
                },
                Source = "Gestionnaire d'identifiants Windows",
                Evidence = Evidence.FromText("CredentialEntry", entry.TargetName,
                    $"{entry.TargetName} · utilisateur : {entry.UserName ?? "(non renseigne)"}",
                    "Verifiable a la main : control keymgr.dll"),
            });
        }

        run.Progress(1, "Termine");

        var verdict = matches > 0 ? "OUI" : "NON";

        return Task.FromResult(run.ToResult(
            $"OINK detecte : {verdict} ({entries.Count} entrees examinees, coffre de l'utilisateur courant)"));
    }
}
