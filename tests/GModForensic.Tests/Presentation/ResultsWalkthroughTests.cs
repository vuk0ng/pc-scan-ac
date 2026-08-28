using GModForensic.Abstractions;
using GModForensic.Detection;
using GModForensic.Presentation;
using GModForensic.Presentation.Demo;
using GModForensic.Presentation.Services;
using Xunit;
using Xunit.Abstractions;

namespace GModForensic.Tests.Presentation;

/// <summary>
/// Restitue en texte ce que l'ecran de resultats affiche et ce que l'export produit.
/// <para>
/// Lancer avec : <c>dotnet test --filter Walkthrough --logger "console;verbosity=detailed"</c>
/// </para>
/// </summary>
public sealed class ResultsWalkthroughTests
{
    private readonly ITestOutputHelper _output;

    public ResultsWalkthroughTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Walkthrough_l_ecran_de_resultats_et_le_resume_exporte()
    {
        var shell = new ShellViewModel(
            new FakeScanSession(),
            new RecordingExporter(),
            new DetectionEngine([new DemoDetectionRule()]));

        shell.Home.OperatorName = "staff.durand";
        shell.Home.SubjectIdentifier = "joueur#4412";
        shell.Home.ConsentGiven = true;

        await shell.StartScanAsync();

        var results = shell.Results;

        _output.WriteLine("SCAN TERMINE");
        _output.WriteLine(string.Empty);
        _output.WriteLine($"  Score de suspicion : {results.ScoreValue} / 100   —   {results.BandLabel}");
        _output.WriteLine($"  {results.BandGuidance}");
        _output.WriteLine(string.Empty);
        _output.WriteLine(
            $"  Critiques : {results.Score.CriticalCount}    Eleves : {results.Score.HighCount}"
            + $"    Moyens : {results.Score.MediumCount}    Faibles : {results.Score.LowCount}");
        _output.WriteLine(string.Empty);
        _output.WriteLine("  Contribution par categorie (plafonnee a 60) :");

        foreach (var (category, value) in results.Score.ByCategory.OrderByDescending(p => p.Value))
        {
            _output.WriteLine($"    {category,-18} {value,3}");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine("INDICATEURS");

        foreach (var detection in results.Detections)
        {
            _output.WriteLine($"  [{detection.SeverityLabel,-8}] {detection.Name}  ({detection.ScoreText})");
            _output.WriteLine($"             {detection.Path}");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine("DETAIL DE LA DETECTION SELECTIONNEE");

        var selected = results.Detections.First();
        _output.WriteLine($"  {selected.Name}   [{selected.SeverityLabel}]   {selected.ConfidenceLabel}");
        _output.WriteLine($"  {selected.Explanation}");
        _output.WriteLine(string.Empty);
        _output.WriteLine("  Pourquoi ce score :");

        foreach (var line in selected.ScoreLines)
        {
            _output.WriteLine($"    {line}");
        }

        _output.WriteLine($"    Total : {selected.ScoreText}");
        _output.WriteLine(string.Empty);
        _output.WriteLine("  Causes legitimes possibles :");
        _output.WriteLine($"    {selected.FalsePositiveNote}");

        // Export reel, pour montrer ce que le staff recevra.
        var directory = Path.Combine(Path.GetTempPath(), "gmodforensic-walkthrough", Guid.NewGuid().ToString("n"));

        try
        {
            var written = new ReportExporter("0.4.0").Export(new ExportRequest
            {
                OutputDirectory = directory,
                IncludeJson = true,
                IncludeText = true,
                Result = shell.LastResult!,
                Detections = results.Detections.Select(d => d.Detection).ToArray(),
                Score = results.Score,
                Configuration = new ScanConfiguration
                {
                    OperatorName = "staff.durand",
                    SubjectIdentifier = "joueur#4412",
                    ConsentGiven = true,
                },
                CoverageWindow = "2026-08-14 -> 2026-08-28 (14 j, journal USN)",
            });

            _output.WriteLine(string.Empty);
            _output.WriteLine("RESUME EXPORTE (resume.txt)");
            _output.WriteLine(new string('-', 62));
            _output.WriteLine(File.ReadAllText(written[1]));

            Assert.Equal(2, written.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        Assert.True(results.HasDetections);
    }
}
