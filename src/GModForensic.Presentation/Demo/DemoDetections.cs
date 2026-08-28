using GModForensic.Abstractions.Model;
using GModForensic.Detection;
using DetectionRecord = GModForensic.Abstractions.Model.Detection;

namespace GModForensic.Presentation.Demo;

/// <summary>
/// Detections de DEMONSTRATION de l'etape 4.
/// <para>
/// Elles servent uniquement a construire et verifier l'ecran de resultats — filtres, detail,
/// explication du score — avant que le moteur reel n'existe (etapes 6 et 7). Chaque
/// identifiant de regle est prefixe <c>DEMO.</c> : aucune ne peut etre confondue avec une
/// detection reelle dans un rapport. Supprimees a l'etape 6.
/// </para>
/// </summary>
public static class DemoDetections
{
    public static IReadOnlyList<DetectionRecord> Build() =>
    [
        Make("DEMO.EXECUTED_THEN_ERASED", ScanCategory.UsnJournal, Severity.Critical, Confidence.High,
            "Executable lance puis efface",
            @"C:\Users\joueur\AppData\Local\Temp\WindowsUpdate.exe",
            "Journal USN (C:) + Prefetch",
            "Un executable a laisse une trace d'execution dans le Prefetch, puis a ete renomme et supprime "
            + "moins de trois minutes plus tard. Trois sources independantes concordent sur le meme fichier.",
            "Un desinstalleur ou un installeur temporaire produit la meme sequence. Verifier le nom d'origine "
            + "et l'editeur avant toute conclusion.",
            new ScoreBuilder()
                .Add("PREFETCH_PRESENT", "Trace d'execution trouvee dans le Prefetch", 15)
                .Add("USN_DELETED", "Suppression confirmee par le journal USN", 15)
                .Add("RENAMED_BEFORE_DELETE", "Renomme « cheat_loader.exe » -> « WindowsUpdate.exe »", 20)
                .Add("USER_TEMP_PATH", "Situe dans un dossier temporaire utilisateur", 10)),

        Make("DEMO.KERNEL_DRIVER_ATTEMPT", ScanCategory.EventLog, Severity.Critical, Confidence.High,
            "Chargement de pilote bloque par CodeIntegrity",
            @"C:\Users\joueur\Downloads\drv64.sys",
            "Microsoft-Windows-CodeIntegrity/Operational (3033)",
            "Windows a refuse de charger une image dont la signature est invalide. C'est la trace la plus "
            + "directe d'une tentative de chargement d'un composant noyau non signe.",
            "Un pilote materiel ancien ou mal signe declenche le meme evenement. Verifier l'editeur du fichier.",
            new ScoreBuilder()
                .Add("CODEINTEGRITY_BLOCK", "Chargement d'image bloque, signature invalide", 50)),

        Make("DEMO.RENAMED_TO_SYSTEM_LOOKALIKE", ScanCategory.UsnJournal, Severity.High, Confidence.Medium,
            "Fichier renomme en nom de binaire systeme",
            @"C:\Users\joueur\AppData\Local\Temp\svchost.exe",
            "Journal USN (C:)",
            "Un fichier a ete renomme en « svchost.exe » hors de System32. Le nom d'un binaire systeme "
            + "utilise ailleurs que dans son emplacement legitime est un motif de dissimulation courant.",
            "Certains outils de developpement et bacs a sable creent des copies nommees ainsi pour des tests.",
            new ScoreBuilder()
                .Add("SYSTEM_LOOKALIKE", "Nom de binaire systeme hors de System32", 30)),

        Make("DEMO.DOWNLOAD_FROM_CDN", ScanCategory.Downloads, Severity.High, Confidence.Medium,
            "Executable telecharge depuis un CDN de messagerie",
            @"C:\Users\joueur\Downloads\gmod_helper.dll",
            "Flux Zone.Identifier (HostUrl)",
            "Le flux Zone.Identifier du fichier indique une origine cdn.discordapp.com. L'URL source exacte "
            + "est conservee comme preuve, sans avoir eu a lire la moindre donnee de Discord.",
            "Le partage de fichiers legitimes (mods, addons, outils) via Discord est massivement courant. "
            + "Seule la nature du fichier telecharge compte.",
            new ScoreBuilder()
                .Add("ZONE_CDN_ORIGIN", "Origine cdn.discordapp.com confirmee par Zone.Identifier", 20)
                .Add("EXECUTABLE_EXTENSION", "Extension executable", 10)),

        Make("DEMO.USB_TRANSFER_WINDOW", ScanCategory.RemovableDevices, Severity.High, Confidence.Low,
            "Creations de fichiers juste apres une connexion USB",
            @"C:\Users\joueur\Desktop\build\",
            "Registre USBSTOR + journal USN (C:)",
            "Une cle USB a ete connectee, puis onze fichiers ont ete crees dans les cinq minutes. "
            + "La correlation temporelle est etablie ; elle ne dit rien du contenu transfere.",
            "Copier des fichiers depuis une cle USB est parfaitement banal. Cet indicateur n'a de valeur "
            + "qu'associe a la nature des fichiers crees.",
            new ScoreBuilder()
                .Add("USB_THEN_CREATE", "Connexion USB suivie de creations de fichiers", 30)),

        Make("DEMO.PREFETCH_ORPHAN", ScanCategory.Prefetch, Severity.Medium, Confidence.Medium,
            "Trace Prefetch sans executable correspondant",
            @"C:\Windows\Prefetch\INJECTOR.EXE-2B7F1A44.pf",
            "Prefetch",
            "Un fichier Prefetch atteste de deux executions, mais l'executable n'existe plus sur le disque.",
            "Les installeurs et desinstalleurs laissent normalement des traces Prefetch orphelines. "
            + "Le chemin d'origine et la correlation USN font la difference.",
            new ScoreBuilder()
                .Add("PREFETCH_ORPHAN", "Prefetch present, executable absent", 15)),

        Make("DEMO.ANTIFORENSIC_USN_RESET", ScanCategory.AntiForensic, Severity.Medium, Confidence.Medium,
            "Journal USN recree recemment",
            "C:",
            "FSCTL_QUERY_USN_JOURNAL",
            "Le journal USN porte un identifiant cree apres la derniere mise sous tension : il a donc ete "
            + "supprime puis recree. La couverture temporelle en est fortement reduite.",
            "Une reinstallation de Windows, un redimensionnement de partition ou un outil de maintenance "
            + "produisent le meme effet. Windows a ete installe il y a 412 jours sur cette machine.",
            new ScoreBuilder()
                .Add("USN_JOURNAL_RESET", "Journal USN recree apres le dernier demarrage", 15)),

        Make("DEMO.UNSIGNED_DLL_TEMP", ScanCategory.FileSystem, Severity.Low, Confidence.Low,
            "DLL non signee dans un dossier temporaire",
            @"C:\Users\joueur\AppData\Local\Temp\d3d9hook.dll",
            "Systeme de fichiers",
            "Une bibliotheque non signee reside dans un dossier temporaire.",
            "Les dossiers temporaires contiennent legitimement des DLL non signees : outils de developpement, "
            + "runtimes, extractions d'installeurs.",
            new ScoreBuilder().Add("UNSIGNED_IN_TEMP", "DLL non signee dans un chemin temporaire", 5)),

        Make("DEMO.SUSPICIOUS_NAME", ScanCategory.FileSystem, Severity.Low, Confidence.Low,
            "Nom de fichier figurant dans la liste d'indicateurs",
            @"C:\Users\joueur\Documents\projets\loader.exe",
            "Systeme de fichiers",
            "Le nom correspond a un motif de la liste d'indicateurs. Pondere tres bas volontairement.",
            "Un cheat reellement utilise porte rarement son nom. Dossiers de developpement, projets "
            + "d'apprentissage et outils legitimes declenchent constamment ce motif. Ne jamais sanctionner "
            + "sur le nom seul.",
            new ScoreBuilder().Add("NAME_PATTERN", "Nom correspondant a un motif d'indicateur", 5)),

        Make("DEMO.RECENT_LNK_MISSING_TARGET", ScanCategory.RecentFiles, Severity.Low, Confidence.Low,
            "Raccourci recent dont la cible n'existe plus",
            @"C:\Users\joueur\Downloads\build_final.zip",
            "shell:recent (.lnk)",
            "Un raccourci de la liste des fichiers recents pointe vers une archive absente du disque.",
            "Supprimer une archive apres extraction est le comportement normal.",
            new ScoreBuilder().Add("LNK_DEAD_TARGET", "Cible du raccourci absente", 5)),

        Make("DEMO.WINRAR_EXTRACTION", ScanCategory.Archives, Severity.Low, Confidence.Low,
            "Extraction d'archive vers un dossier temporaire",
            @"C:\Users\joueur\AppData\Local\Temp\gm_pack",
            @"HKCU\SOFTWARE\WinRAR\DialogEditHistory\ExtrPath",
            "L'historique d'extraction de WinRAR indique un depot recent dans un dossier temporaire.",
            "Extraire une archive vers un dossier temporaire est un usage courant. Sans horodatage par "
            + "entree, seule la position dans l'historique est exploitable.",
            new ScoreBuilder().Add("EXTRACT_TO_TEMP", "Extraction vers un dossier temporaire", 5)),

        Make("DEMO.EXPLORER_RESTART", ScanCategory.Processes, Severity.Low, Confidence.Low,
            "Explorer redemarre peu avant le controle",
            @"C:\Windows\explorer.exe",
            "GetProcessTimes + journal Application",
            "Explorer tourne depuis 2 minutes alors que la session est ouverte depuis 3 h 41. "
            + "Aucun plantage correspondant dans le journal Application.",
            "Un redemarrage recent d'Explorer a de nombreuses causes legitimes : changement de DPI ou de "
            + "theme, mise a jour, installation d'une extension shell, redemarrage manuel. "
            + "C'est un indicateur a verifier, jamais une preuve.",
            new ScoreBuilder().Add("EXPLORER_YOUNG", "Explorer nettement plus jeune que la session", 5)),
    ];

    private static DetectionRecord Make(
        string ruleId,
        ScanCategory category,
        Severity severity,
        Confidence confidence,
        string name,
        string path,
        string source,
        string explanation,
        string falsePositiveNote,
        ScoreBuilder score) => new()
        {
            RuleId = ruleId,
            Category = category,
            Severity = severity,
            Confidence = confidence,
            Name = name,
            Description = explanation,
            Path = path,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(5, 4000)),
            Source = source,
            Evidence =
            [
                Evidence.FromText(
                    "Demo",
                    path,
                    "Donnee de demonstration — le contenu reel de la preuve arrive avec les modules (etape 5).",
                    "Les modules reels fourniront ici la valeur brute et son emplacement de verification."),
            ],
            Score = score.Build(),
            Explanation = explanation,
            FalsePositiveNote = falsePositiveNote,
        };
}

/// <summary>Regle de demonstration : injecte les detections d'exemple, quelles que soient les observations.</summary>
public sealed class DemoDetectionRule : IDetectionRule
{
    private readonly IReadOnlyList<DetectionRecord> _detections = DemoDetections.Build();
    private bool _emitted;

    public string Id => "DEMO.CATALOG";

    public string FalsePositiveNote =>
        "Regle de demonstration de l'etape 4. Elle ne lit aucun artefact et disparait a l'etape 6.";

    public IEnumerable<DetectionRecord> Evaluate(CorrelatedEntity entity)
    {
        if (_emitted)
        {
            yield break;
        }

        _emitted = true;

        foreach (var detection in _detections)
        {
            yield return detection;
        }
    }
}
