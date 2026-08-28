using GModForensic.Abstractions;
using GModForensic.Detection;
using GModForensic.Presentation;
using GModForensic.Presentation.Demo;
using GModForensic.Presentation.Services;
using Xunit;

namespace GModForensic.Tests.Presentation;

/// <summary>
/// Ecrit un rapport JSON d'exemple dans le dossier indique par la variable d'environnement
/// <c>GMODFORENSIC_SAMPLE_DIR</c>.
/// <para>
/// Sert a alimenter le lecteur de rapport (<c>tools/report-reader.html</c>) avec des donnees
/// reelles, et a verifier que le format reste consommable. Sans la variable, le test se
/// contente de valider la structure dans un dossier temporaire.
/// </para>
/// </summary>
public sealed class SampleReportTests
{
    [Fact]
    public async Task Le_rapport_json_contient_les_sections_attendues_par_le_lecteur()
    {
        var shell = new ShellViewModel(
            new FakeScanSession(),
            new RecordingExporter(),
            new DetectionEngine([new DemoDetectionRule()]));

        shell.Home.OperatorName = "staff.durand";
        shell.Home.SubjectIdentifier = "joueur#4412";
        shell.Home.ConsentGiven = true;

        await shell.StartScanAsync();

        var directory = Environment.GetEnvironmentVariable("GMODFORENSIC_SAMPLE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "gmodforensic-sample", Guid.NewGuid().ToString("n"));

        var written = new ReportExporter("0.4.0").Export(new ExportRequest
        {
            OutputDirectory = directory,
            IncludeJson = true,
            IncludeText = true,
            Result = shell.LastResult!,
            Detections = shell.Results.Detections.Select(d => d.Detection).ToArray(),
            Score = shell.Results.Score,
            Configuration = new ScanConfiguration
            {
                OperatorName = "staff.durand",
                SubjectIdentifier = "joueur#4412",
                ConsentGiven = true,
                Profile = ScanProfile.Standard,
            },
            CoverageWindow = "2026-08-14 -> 2026-08-28 (14 j, journal USN sur C:)",
        });

        var json = File.ReadAllText(written[0]);

        // Sections consommees par le lecteur de rapport : leur absence casserait le tableau de bord.
        foreach (var section in new[]
                 {
                     "schemaVersion", "disclaimer", "metadata", "score",
                     "modules", "detections", "observations", "executionLog", "accessLog",
                 })
        {
            Assert.Contains($"\"{section}\"", json, StringComparison.Ordinal);
        }
    }
}
