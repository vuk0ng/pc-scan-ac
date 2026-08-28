using GModForensic.Abstractions.Model;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Reporting;

/// <summary>Metadonnees de tracabilite du controle (§23).</summary>
public sealed record ReportMetadata
{
    public required string ScanId { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required string ScannerVersion { get; init; }
    public required string MachineName { get; init; }
    public string? OperatorName { get; init; }
    public string? SubjectIdentifier { get; init; }
    public required bool ConsentGiven { get; init; }
    public required bool WasCancelled { get; init; }
    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// Fenetre temporelle reellement couverte par les sources. Affichee a cote du score :
    /// un 0/100 sur deux jours de journal USN ne dit pas la meme chose que sur trente.
    /// </summary>
    public required string CoverageWindow { get; init; }
}

public sealed record ReportScore
{
    public required int Value { get; init; }
    public required string Label { get; init; }
    public required string Guidance { get; init; }
    public required int Critical { get; init; }
    public required int High { get; init; }
    public required int Medium { get; init; }
    public required int Low { get; init; }
}

public sealed record ReportModule
{
    public required string ModuleId { get; init; }
    public required string Status { get; init; }
    public required string Symbol { get; init; }
    public required int ItemsExamined { get; init; }
    public required int Observations { get; init; }
    public required double ElapsedSeconds { get; init; }
    public string? StatusReason { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
}

public sealed record ReportAccessEntry
{
    public required string Timestamp { get; init; }
    public required string ModuleId { get; init; }
    public required string ResourceKind { get; init; }
    public required string Resource { get; init; }
}

/// <summary>Rapport complet. Le format JSON est versionne et stable.</summary>
public sealed record ForensicReport
{
    public string SchemaVersion { get; init; } = "0.4";

    /// <summary>Clause fixe, presente dans chaque rapport (§21, §23).</summary>
    public string Disclaimer { get; init; } =
        "Ce document recense des indicateurs. Aucun element ci-dessous ne constitue a lui seul "
        + "une preuve d'utilisation de cheat. Toute conclusion doit reposer sur une verification manuelle.";

    public required ReportMetadata Metadata { get; init; }
    public required ReportScore Score { get; init; }
    public required IReadOnlyList<ReportModule> Modules { get; init; }
    public required IReadOnlyList<DetectionRecord> Detections { get; init; }

    /// <summary>Toutes les observations, y compris celles n'ayant produit aucune detection (§1).</summary>
    public required IReadOnlyList<Observation> Observations { get; init; }

    public required IReadOnlyList<string> ExecutionLog { get; init; }

    /// <summary>
    /// Chaque ressource effectivement lue. C'est la transparence du §1 : le staff comme la
    /// personne analysee peuvent verifier a posteriori ce que le programme a touche.
    /// </summary>
    public required IReadOnlyList<ReportAccessEntry> AccessLog { get; init; }
}
