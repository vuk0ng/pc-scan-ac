namespace GModForensic.Abstractions.Model;

/// <summary>
/// Incident rencontre par un module : acces refuse, cle absente, processus disparu.
/// Un diagnostic n'interrompt jamais un scan ; il explique pourquoi une information manque (§25).
/// </summary>
public sealed record Diagnostic
{
    public required DiagnosticLevel Level { get; init; }

    /// <summary>Ressource concernee : chemin, cle de registre, PID.</summary>
    public required string Resource { get; init; }

    public required string Message { get; init; }

    /// <summary>Code d'erreur Win32 d'origine, quand il en existe un.</summary>
    public int? Win32ErrorCode { get; init; }

    public static Diagnostic AccessDenied(string resource, int? win32Code = null) => new()
    {
        Level = DiagnosticLevel.Warning,
        Resource = resource,
        Message = "Acces refuse.",
        Win32ErrorCode = win32Code,
    };

    public static Diagnostic NotFound(string resource) => new()
    {
        // L'absence d'une cle ou d'un fichier est normale, pas une erreur.
        Level = DiagnosticLevel.Info,
        Resource = resource,
        Message = "Ressource absente.",
    };

    public static Diagnostic Error(string resource, string message, int? win32Code = null) => new()
    {
        Level = DiagnosticLevel.Error,
        Resource = resource,
        Message = message,
        Win32ErrorCode = win32Code,
    };
}
