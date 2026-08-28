using System.Globalization;
using System.Text;

namespace GModForensic.Reporting;

/// <summary>
/// Resume condense, destine au collage dans un ticket ou un salon staff.
/// Il renvoie toujours vers le rapport complet : il ne se suffit pas a lui-meme.
/// </summary>
public sealed class TextReportWriter
{
    private const int TopDetections = 10;

    public string Write(ForensicReport report, ReportOutputWriter output, string fileName = "resume.txt")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        var builder = new StringBuilder();

        builder.AppendLine("SCANNER FORENSIC GMod — RESUME");
        builder.AppendLine(new string('=', 60));
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Sujet        : {report.Metadata.SubjectIdentifier ?? "(non renseigne)"}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Staff        : {report.Metadata.OperatorName ?? "(non renseigne)"}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Date (UTC)   : {report.Metadata.GeneratedUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Duree        : {report.Metadata.ElapsedSeconds:0.0} s");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Couverture   : {report.Metadata.CoverageWindow}");

        if (report.Metadata.WasCancelled)
        {
            builder.AppendLine("ATTENTION    : scan interrompu — couverture incomplete.");
        }

        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"SCORE : {report.Score.Value}/100 — {report.Score.Label}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"        {report.Score.Guidance}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Critiques {report.Score.Critical}  ·  Eleves {report.Score.High}  ·  "
            + $"Moyens {report.Score.Medium}  ·  Faibles {report.Score.Low}");
        builder.AppendLine();

        builder.AppendLine("MODULES");

        foreach (var module in report.Modules)
        {
            var reason = module.StatusReason is null ? string.Empty : $"  — {module.StatusReason}";
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  {module.Symbol}  {module.ModuleId,-14}{module.ItemsExamined,7} elements{reason}");
        }

        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"INDICATEURS (au plus {TopDetections})");

        if (report.Detections.Count == 0)
        {
            builder.AppendLine("  Aucun indicateur retenu sur la fenetre couverte.");
        }

        foreach (var detection in report.Detections
                     .OrderByDescending(d => d.Severity)
                     .ThenByDescending(d => d.Score.Total)
                     .Take(TopDetections))
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  [{detection.Severity}] {detection.Name} — {detection.Score.Total}/100");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      {detection.Path ?? "(sans chemin)"}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      source : {detection.Source}");
        }

        builder.AppendLine();
        builder.AppendLine(report.Disclaimer);
        builder.AppendLine("Rapport complet : rapport.json");

        return output.WriteText(fileName, builder.ToString());
    }
}
