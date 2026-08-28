using GModForensic.Abstractions;
using GModForensic.Detection.Scoring;
using GModForensic.Engine;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Presentation.Services;

public sealed record ExportRequest
{
    public required string OutputDirectory { get; init; }
    public required bool IncludeJson { get; init; }
    public required bool IncludeText { get; init; }
    public required ScanRunResult Result { get; init; }
    public required IReadOnlyList<DetectionRecord> Detections { get; init; }
    public required GlobalScore Score { get; init; }
    public required ScanConfiguration Configuration { get; init; }
    public string CoverageWindow { get; init; } = "fenetre non determinee";
}

public interface IReportExporter
{
    /// <summary>Ecrit les fichiers demandes et retourne leurs chemins.</summary>
    IReadOnlyList<string> Export(ExportRequest request);
}
