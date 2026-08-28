using System.Globalization;
using GModForensic.Reporting;

namespace GModForensic.Presentation.Services;

/// <summary>Traduit le resultat d'un scan en rapport, puis delegue l'ecriture a Reporting.</summary>
public sealed class ReportExporter : IReportExporter
{
    private readonly string _version;
    private readonly TimeProvider _clock;

    public ReportExporter(string version, TimeProvider? clock = null)
    {
        _version = version;
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<string> Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var report = BuildReport(request);

        // ReportOutputWriter est le seul point d'ecriture du produit, et il contraint
        // toute cible au dossier de sortie choisi par le staff.
        var output = new ReportOutputWriter(request.OutputDirectory);
        var written = new List<string>();

        if (request.IncludeJson)
        {
            written.Add(new JsonReportWriter().Write(report, output));
        }

        if (request.IncludeText)
        {
            written.Add(new TextReportWriter().Write(report, output));
        }

        return written;
    }

    private ForensicReport BuildReport(ExportRequest request) => new()
    {
        Metadata = new ReportMetadata
        {
            ScanId = request.Result.ScanId,
            GeneratedUtc = _clock.GetUtcNow(),
            ScannerVersion = _version,
            MachineName = Environment.MachineName,
            OperatorName = request.Configuration.OperatorName,
            SubjectIdentifier = request.Configuration.SubjectIdentifier,
            ConsentGiven = request.Configuration.ConsentGiven,
            WasCancelled = request.Result.WasCancelled,
            ElapsedSeconds = request.Result.Elapsed.TotalSeconds,
            CoverageWindow = request.CoverageWindow,
        },
        Score = new ReportScore
        {
            Value = request.Score.Value,
            Label = request.Score.Band.Label,
            Guidance = request.Score.Band.Guidance,
            Critical = request.Score.CriticalCount,
            High = request.Score.HighCount,
            Medium = request.Score.MediumCount,
            Low = request.Score.LowCount,
            ByCategory = request.Score.ByCategory.ToDictionary(
                pair => pair.Key.ToString(), pair => pair.Value, StringComparer.Ordinal),
        },
        Modules = request.Result.ModuleResults
            .Select(m => new ReportModule
            {
                ModuleId = m.ModuleId,
                Status = m.Status.ToString(),
                Symbol = m.StatusSymbol,
                ItemsExamined = m.ItemsExamined,
                Observations = m.Observations.Count,
                ElapsedSeconds = m.Elapsed.TotalSeconds,
                StatusReason = m.StatusReason,
                Diagnostics = m.Diagnostics,
            })
            .ToArray(),
        Detections = request.Detections,
        Observations = request.Result.Observations.ToArray(),
        ExecutionLog = request.Result.Log.Select(e => e.ToString()).ToArray(),
        AccessLog = request.Result.AccessLog
            .Select(a => new ReportAccessEntry
            {
                Timestamp = a.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                ModuleId = a.ModuleId,
                ResourceKind = a.ResourceKind,
                Resource = a.Resource,
            })
            .ToArray(),
    };
}
