using GModForensic.Abstractions;
using GModForensic.Abstractions.Model;
using GModForensic.Detection;
using GModForensic.Detection.Scoring;
using GModForensic.Presentation;
using GModForensic.Presentation.Demo;
using GModForensic.Presentation.Services;
using Xunit;

namespace GModForensic.Tests.Presentation;

public sealed class ResultsAndExportTests
{
    private static async Task<ShellViewModel> RunDemoScanAsync(RecordingExporter? exporter = null)
    {
        var shell = new ShellViewModel(
            new FakeScanSession(),
            exporter ?? new RecordingExporter(),
            new DetectionEngine([new DemoDetectionRule()]));

        shell.Home.OperatorName = "staff.durand";
        shell.Home.SubjectIdentifier = "joueur#4412";
        shell.Home.ConsentGiven = true;

        await shell.StartScanAsync();
        return shell;
    }

    [Fact]
    public async Task Le_filtre_de_gravite_reduit_la_liste_sans_perdre_les_detections()
    {
        var shell = await RunDemoScanAsync();
        var results = shell.Results;

        var total = results.Detections.Count;
        Assert.True(total > 0);

        results.SeverityFilter = Severity.Critical;
        Assert.All(results.Detections, d => Assert.Equal(Severity.Critical, d.Severity));
        Assert.True(results.Detections.Count < total);

        results.ClearFiltersCommand.Execute(null);
        Assert.Equal(total, results.Detections.Count);
    }

    [Fact]
    public async Task La_recherche_plein_texte_porte_sur_le_chemin_et_la_source()
    {
        var shell = await RunDemoScanAsync();
        var results = shell.Results;

        results.SearchText = "Zone.Identifier";

        Assert.NotEmpty(results.Detections);
        Assert.All(results.Detections, d =>
            Assert.Contains("Zone.Identifier", d.SearchIndex, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Un_filtre_sans_resultat_explique_pourquoi_la_liste_est_vide()
    {
        var shell = await RunDemoScanAsync();
        var results = shell.Results;

        results.SearchText = "chaine-qui-n-existe-pas-dans-les-donnees";

        Assert.False(results.HasDetections);
        Assert.Contains("filtre", results.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Les_detections_sont_triees_des_plus_graves_aux_moins_graves()
    {
        var shell = await RunDemoScanAsync();

        var severities = shell.Results.Detections.Select(d => d.Severity).ToList();

        for (var i = 1; i < severities.Count; i++)
        {
            Assert.True(severities[i] <= severities[i - 1], "La liste n'est pas triee par gravite.");
        }
    }

    [Fact]
    public async Task Chaque_detection_expose_son_detail_de_score_et_ses_causes_legitimes()
    {
        var shell = await RunDemoScanAsync();

        foreach (var detection in shell.Results.Detections)
        {
            // §21 : le score doit toujours pouvoir etre explique ligne a ligne.
            Assert.NotEmpty(detection.ScoreLines);
            // §22 : aucune detection ne peut exister sans ses causes legitimes documentees.
            Assert.False(string.IsNullOrWhiteSpace(detection.FalsePositiveNote));
            Assert.False(string.IsNullOrWhiteSpace(detection.Explanation));
        }
    }

    [Fact]
    public async Task L_export_transmet_le_scan_et_les_detections_au_redacteur()
    {
        var exporter = new RecordingExporter();
        var shell = await RunDemoScanAsync(exporter);

        shell.Export.OutputDirectory = Path.Combine(Path.GetTempPath(), "gmodforensic-export");
        shell.Export.ExportCommand.Execute(null);

        Assert.NotNull(exporter.LastRequest);
        Assert.Equal(shell.LastResult, exporter.LastRequest!.Result);
        Assert.Equal(shell.Results.Detections.Count, exporter.LastRequest.Detections.Count);
        Assert.Equal("joueur#4412", exporter.LastRequest.Configuration.SubjectIdentifier);
        Assert.Equal(2, shell.Export.WrittenFiles.Count);
        Assert.False(shell.Export.LastExportFailed);
    }

    [Fact]
    public async Task Un_echec_d_ecriture_est_signale_sans_faire_perdre_les_resultats()
    {
        var exporter = new RecordingExporter { ShouldFail = true };
        var shell = await RunDemoScanAsync(exporter);

        shell.Export.ExportCommand.Execute(null);

        Assert.True(shell.Export.LastExportFailed);
        Assert.Contains("disque plein", shell.Export.StatusMessage!, StringComparison.Ordinal);

        // Les resultats restent intacts et l'export reste retentable ailleurs.
        Assert.True(shell.Results.HasDetections);
        Assert.True(shell.Export.CanExport);
    }

    [Fact]
    public async Task L_export_reel_ecrit_un_json_et_un_resume_lisibles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gmodforensic-tests", Guid.NewGuid().ToString("n"));
        var shell = await RunDemoScanAsync();

        try
        {
            var exporter = new ReportExporter("0.4.0");

            var written = exporter.Export(new ExportRequest
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
                },
            });

            Assert.Equal(2, written.Count);

            var json = File.ReadAllText(written[0]);
            Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
            Assert.Contains("DEMO.EXECUTED_THEN_ERASED", json, StringComparison.Ordinal);
            // La clause figure dans chaque rapport, sans exception.
            Assert.Contains("ne constitue a lui seul", json, StringComparison.Ordinal);

            var summary = File.ReadAllText(written[1]);
            Assert.Contains("SCORE :", summary, StringComparison.Ordinal);
            Assert.Contains("joueur#4412", summary, StringComparison.Ordinal);
            Assert.Contains("ne constitue a lui seul", summary, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Une_notification_de_progression_perimee_ne_fait_pas_reculer_la_barre()
    {
        var scan = new ScanViewModel();
        scan.Reset([]);

        scan.ApplyProgress(Snapshot(sequence: 10, fraction: 0.6));
        Assert.Equal(0.6, scan.OverallFraction, precision: 6);

        // Livraison dans le desordre : la notification plus ancienne doit etre ignoree.
        scan.ApplyProgress(Snapshot(sequence: 4, fraction: 0.2));
        Assert.Equal(0.6, scan.OverallFraction, precision: 6);

        scan.ApplyProgress(Snapshot(sequence: 11, fraction: 0.75));
        Assert.Equal(0.75, scan.OverallFraction, precision: 6);
    }

    private static GModForensic.Engine.ScanProgressSnapshot Snapshot(long sequence, double fraction) => new()
    {
        Sequence = sequence,
        OverallFraction = fraction,
        CurrentStep = "test",
        Elapsed = TimeSpan.FromSeconds(1),
        ItemsExamined = 1,
        ObservationsCollected = 0,
        Modules = [],
    };
}

public sealed class ScoreAggregatorTests
{
    [Fact]
    public void Un_seul_indicateur_critique_ne_sature_jamais_le_score()
    {
        var score = ScoreAggregator.Compute([Detection(Severity.Critical, Confidence.High, 50, "R1")]);

        // Coherent avec « aucun element n'est une preuve a lui seul ».
        Assert.Equal(50, score.Value);
        Assert.Equal(1, score.CriticalCount);
    }

    [Fact]
    public void Deux_indicateurs_pesent_plus_qu_un_seul_mais_moins_que_leur_somme()
    {
        var single = ScoreAggregator.Compute([Detection(Severity.High, Confidence.High, 30, "R1")]).Value;

        var pair = ScoreAggregator.Compute(
        [
            Detection(Severity.High, Confidence.High, 30, "R1"),
            Detection(Severity.High, Confidence.High, 30, "R2"),
        ]).Value;

        Assert.Equal(30, single);
        Assert.True(pair > single);
        Assert.True(pair < 60, $"Le score devrait saturer, il vaut {pair}.");
        Assert.Equal(51, pair);
    }

    [Fact]
    public void Une_confiance_faible_reduit_reellement_la_contribution()
    {
        var high = ScoreAggregator.Compute([Detection(Severity.High, Confidence.High, 30, "R1")]).Value;
        var low = ScoreAggregator.Compute([Detection(Severity.High, Confidence.Low, 30, "R1")]).Value;

        Assert.True(low < high);
        Assert.Equal(12, low);
    }

    [Fact]
    public void Cent_detections_de_la_meme_regle_ne_saturent_pas_le_score()
    {
        var spam = Enumerable
            .Range(0, 100)
            .Select(_ => Detection(Severity.Low, Confidence.Medium, 5, "SAME_RULE"))
            .ToArray();

        var score = ScoreAggregator.Compute(spam).Value;

        // Rendements decroissants : sans eux, un dossier Temp bien rempli suffirait a accuser.
        Assert.True(score < 25, $"Le spam d'une meme regle atteint {score}.");
    }

    [Fact]
    public void Une_seule_categorie_ne_peut_pas_depasser_son_plafond()
    {
        var many = Enumerable
            .Range(0, 12)
            .Select(i => Detection(Severity.Critical, Confidence.High, 50, $"R{i}", ScanCategory.UsnJournal))
            .ToArray();

        var score = ScoreAggregator.Compute(many);

        // Un score eleve doit exiger des sources de natures differentes.
        Assert.True(score.Value <= ScoreAggregator.CategoryCap);
        Assert.Equal(ScoreAggregator.CategoryCap, score.ByCategory[ScanCategory.UsnJournal]);
    }

    [Theory]
    [InlineData(0, "Aucun indicateur notable")]
    [InlineData(25, "Indicateurs faibles")]
    [InlineData(45, "Indicateurs moderes")]
    [InlineData(72, "Indicateurs eleves")]
    [InlineData(95, "Indicateurs tres eleves")]
    public void Les_bandes_ne_parlent_jamais_de_triche(int value, string expected)
    {
        var band = ScoreAggregator.BandFor(value);

        Assert.Equal(expected, band.Label);
        Assert.DoesNotContain("trich", band.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trich", band.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aucune_detection_donne_un_score_nul()
    {
        var score = ScoreAggregator.Compute([]);

        Assert.Equal(0, score.Value);
        Assert.Equal("Aucun indicateur notable", score.Band.Label);
    }

    private static Abstractions.Model.Detection Detection(
        Severity severity,
        Confidence confidence,
        int points,
        string ruleId,
        ScanCategory category = ScanCategory.FileSystem) => new()
        {
            RuleId = ruleId,
            Category = category,
            Severity = severity,
            Confidence = confidence,
            Name = "test",
            Description = "test",
            Source = "test",
            Evidence = [],
            Score = new ScoreBuilder().Add(ruleId, "test", points).Build(),
            Explanation = "test",
            FalsePositiveNote = "test",
        };
}
