# Étape 2 — Architecture technique complète

## 1. Vue d'ensemble

```
┌──────────────────────────────────────────────────────────────────────────┐
│  GModForensic.App   (WPF, MVVM)                                          │
│  Accueil/Config → Scan (progression, annulation) → Résultats → Export    │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │ IScanOrchestrator (async, IProgress, CT)
┌───────────────────────────────▼──────────────────────────────────────────┐
│  GModForensic.Engine                                                     │
│  ScanOrchestrator · ModuleHost (isolation + timeout) · ScanContext       │
└──────┬──────────────────────────────────────────────┬────────────────────┘
       │ Observation[]                                │ Detection[]
┌──────▼───────────────────────────┐   ┌──────────────▼────────────────────┐
│  GModForensic.Scanners           │   │  GModForensic.Detection           │
│  17 modules IScanModule          │   │  Normalizer · Correlator          │
│  (aucun jugement, que des faits) │   │  RuleEngine  · ScoreAggregator    │
└──────┬───────────────────────────┘   │  KnowledgeBase (listes JSON)      │
       │                               └──────────────┬────────────────────┘
┌──────▼───────────────────────────┐   ┌──────────────▼────────────────────┐
│  GModForensic.Native             │   │  GModForensic.Reporting           │
│  CsWin32 · SafeHandle · parseurs │   │  JSON · HTML autonome · TXT       │
└──────────────────────────────────┘   └───────────────────────────────────┘
                    │
┌───────────────────▼──────────────────────────────────────────────────────┐
│  GModForensic.Abstractions   (contrats purs, testables, sans Win32)      │
└──────────────────────────────────────────────────────────────────────────┘
```

Règle de dépendance : `Abstractions` ne dépend de rien. `Scanners` ne dépend jamais de `Detection`
(un module ne peut pas savoir comment il sera jugé). `App` ne dépend jamais de `Native`.

---

## 2. Arborescence du dépôt

```
GModForensicScanner.sln
Directory.Build.props              # TFM, nullable, warnings-as-errors, analyzers
BannedSymbols.txt                  # RÈGLE ABSOLUE appliquée à la compilation
src/
├─ GModForensic.Abstractions/
│  ├─ IScanModule.cs               ScanContext.cs  ModuleResult.cs
│  ├─ Model/Observation.cs  Detection.cs  Evidence.cs  FileKey.cs
│  ├─ Model/Severity.cs  Confidence.cs  ModuleStatus.cs  ScanCategory.cs
│  ├─ Capabilities.cs              # ce que les privilèges obtenus autorisent
│  └─ Logging/IScanLogger.cs
├─ GModForensic.Native/
│  ├─ NativeMethods.txt            # liste des API générées par CsWin32
│  ├─ Io/SafeFileReader.cs  AlternateDataStreams.cs  VolumeMap.cs
│  ├─ Processes/ProcessEnumerator.cs  ServiceResolver.cs  ModuleEnumerator.cs
│  ├─ Memory/ProcessMemoryReader.cs  StringHarvester.cs
│  ├─ Storage/UsnJournalReader.cs  UsnRecordParser.cs  MftNameResolver.cs
│  ├─ Prefetch/PrefetchParser.cs  XpressHuffmanDecompressor.cs
│  ├─ ShellLink/LnkParser.cs  JumpListParser.cs
│  ├─ Security/AuthenticodeVerifier.cs  TokenInspector.cs  PrivilegeActivator.cs
│  ├─ Credentials/CredentialEnumerator.cs
│  └─ Devices/DeviceEnumerator.cs  SetupApiLogParser.cs
├─ GModForensic.Scanners/          # 1 fichier = 1 module (voir docs/03)
├─ GModForensic.Detection/
│  ├─ Normalization/PathNormalizer.cs  FileKeyBuilder.cs
│  ├─ Correlation/EntityCorrelator.cs
│  ├─ Rules/IDetectionRule.cs  JsonRuleLoader.cs  Composite/*.cs
│  ├─ Scoring/ScoreAggregator.cs  ScoreBreakdown.cs
│  └─ Knowledge/KnowledgeBase.cs
├─ GModForensic.Engine/
│  ├─ ScanOrchestrator.cs  ModuleHost.cs  ScanProgress.cs
│  └─ FileFactsCache.cs            # hash/signature/metadata mémorisés
├─ GModForensic.Reporting/
│  ├─ ReportModel.cs  JsonReportWriter.cs  HtmlReportWriter.cs  TextReportWriter.cs
│  ├─ ReportOutputWriter.cs        # SEUL composant autorisé à écrire sur disque
│  └─ Templates/report.template.html
└─ GModForensic.App/
   ├─ app.manifest                 # requireAdministrator, longPathAware, PMv2
   ├─ Views/  ViewModels/  Themes/  Services/
data/rules/
├─ known-software.json  whitelist.json  blacklist.json
├─ cheat-indicators.json  memory-patterns.json  gmod.json
tests/GModForensic.Tests/
├─ Parsers/  (échantillons binaires figés : .pf, .lnk, records USN)
├─ Detection/ (scénarios de scoring, non-régression faux positifs)
└─ Safety/   (vérifie l'absence d'API interdites dans les binaires)
docs/
```

---

## 3. Contrats de données

### 3.1 Observation — un fait, jamais un jugement

```csharp
public sealed record Observation
{
    public required string ModuleId      { get; init; }  // "usn", "prefetch", ...
    public required ObservationKind Kind { get; init; }  // FileCreated, FileRenamed, ProcessRunning...
    public required DateTimeOffset? Timestamp { get; init; } // UTC, null si inconnu
    public FileKey?  Subject      { get; init; }  // entité fichier concernée, si applicable
    public string?   SecondaryPath{ get; init; }  // ancien nom lors d'un renommage
    public IReadOnlyDictionary<string, string> Fields { get; init; } // données typées du module
    public required Evidence Evidence { get; init; }     // trace brute vérifiable
    public required string Source { get; init; }         // "USN Journal (C:)", "HKCU\...\BAM"
}
```

### 3.2 FileKey — la clé de corrélation

C'est la pièce centrale : elle permet de rapprocher un enregistrement USN, une entrée Prefetch, une valeur
BAM et une chaîne mémoire qui parlent du **même fichier**, alors qu'aucun n'utilise la même représentation.

```csharp
public sealed record FileKey
{
    public string? FullPath      { get; init; }  // normalisé : lettre de volume, casse, environnement résolu
    public required string FileName { get; init; } // toujours disponible
    public string? Sha256        { get; init; }  // si le fichier existe encore
    public ulong?  MftReference  { get; init; }  // si issu de l'USN
    public string? PrefetchHash  { get; init; }  // si issu du Prefetch
}
```

Normalisation appliquée : `\Device\HarddiskVolume2\…` → `C:\…` (via `QueryDosDevice`),
`\??\`, `\\?\`, `file:///C:/…`, variables `%…%`, `C:\Users\<user>` → chemin réel, casse repliée en invariant.
Deux `FileKey` fusionnent si : même `Sha256`, ou même `FullPath` normalisé, ou même `MftReference` + volume.

### 3.3 Evidence — la traçabilité vers la vérification manuelle

Exigence §1 : *« conserver les preuves permettant au staff de vérifier manuellement »*.

```csharp
public sealed record Evidence
{
    public required string Kind { get; init; }      // "RegistryValue", "UsnRecord", "PrefetchEntry", "MemoryString"
    public required string Locator { get; init; }   // où re-regarder à la main : chemin de clé, offset, PID+adresse
    public string? RawText { get; init; }           // valeur brute (tronquée, avec longueur d'origine)
    public byte[]? RawBytes { get; init; }          // exporté en hex dans le rapport, plafonné
    public string? VerificationHint { get; init; }  // ex : "regedit → HKCU\SOFTWARE\WinRAR\ArcHistory"
}
```

Aucune copie de binaire suspect n'est faite par défaut (risque antivirus, et ce serait une extraction de
données). Seuls hash + métadonnées sont conservés. Une copie éventuelle reste une option explicite du staff.

### 3.4 Detection — le jugement, produit uniquement par le moteur

Conforme au §4 du cahier des charges, enrichi de ce qu'exige le §21 (« expliquer POURQUOI ») :

```csharp
public sealed record Detection
{
    public required string   Id          { get; init; }
    public required string   RuleId      { get; init; }
    public required ScanCategory Category{ get; init; }
    public required Severity Severity    { get; init; }  // Low / Medium / High / Critical
    public required string   Name        { get; init; }
    public required string   Description { get; init; }
    public required Confidence Confidence{ get; init; }  // Low / Medium / High
    public string?           Path        { get; init; }
    public DateTimeOffset?   Timestamp   { get; init; }
    public required string   Source      { get; init; }
    public required IReadOnlyList<Evidence> Evidence     { get; init; }
    public required ScoreBreakdown Score  { get; init; } // détail ligne à ligne du score
    public required string   Explanation  { get; init; } // texte en français, lisible par un non-technicien
    public required string   FalsePositiveNote { get; init; } // causes légitimes connues — TOUJOURS renseigné
    public IReadOnlyList<string> RelatedObservationIds { get; init; }
}
```

`FalsePositiveNote` est **obligatoire** (`required`) : il est impossible de créer une détection sans documenter
ce qui pourrait l'expliquer légitimement. C'est le §22 encodé dans le type.

### 3.5 ModuleResult

```csharp
public sealed record ModuleResult
{
    public required string ModuleId { get; init; }
    public required ModuleStatus Status { get; init; }   // Success ✓ | Partial ⚠ | Failed ✕ | Skipped | Cancelled
    public required IReadOnlyList<Observation> Observations { get; init; }
    public required IReadOnlyList<Diagnostic>  Diagnostics  { get; init; } // accès refusé, clé absente...
    public int ItemsExamined { get; init; }
    public TimeSpan Elapsed  { get; init; }
    public string?  StatusReason { get; init; }  // "Journal USN indisponible sur D: (volume non NTFS)"
}
```

`Partial` est un état de première classe : « j'ai lu 9 des 12 processus, 3 accès refusés » est une information
utile pour le staff, pas une erreur à masquer.

---

## 4. Contrat de module

```csharp
public interface IScanModule
{
    string        Id            { get; }
    string        DisplayName   { get; }   // "Analyse du journal USN"
    ScanCategory  Category      { get; }
    bool          RequiresAdministrator { get; }
    RequiredCapabilities Requires { get; } // NtfsVolume, ProcessMemory, SecurityLog...
    int           Weight        { get; }   // poids relatif pour la barre de progression

    Task<ModuleResult> RunAsync(ScanContext context, CancellationToken ct);
}
```

Le `ScanContext` fournit : configuration, `IScanLogger`, `IProgress<ModuleProgress>`, `FileFactsCache`
(hash/signature partagés entre modules — un fichier n'est jamais hashé deux fois), `Capabilities` réellement
obtenues, et l'horloge (injectable, pour tests déterministes).

---

## 5. Cycle de vie d'un scan

```
1. Démarrage           app.manifest → UAC → élévation
2. Inventaire          TokenInspector : élévation ? SeDebugPrivilege ? SeSecurityPrivilege ?
                       → Capabilities ; les modules non satisfaits passent en Skipped avec motif affiché
3. Consentement        écran : nom du staff, identifiant du sujet, case de consentement
4. Planification       tri des modules : rapides et sans I/O disque d'abord (retour visuel immédiat)
5. Exécution           ModuleHost : Parallel avec MaxDegreeOfParallelism = 4 (I/O bound)
                       chaque module : try/catch global + timeout (défaut 180 s) + CT lié
                       un module qui échoue → Failed, le scan continue TOUJOURS
6. Agrégation          Observations → Normalizer → Correlator → RuleEngine → Detections
7. Scoring             ScoreAggregator → score global + répartition par sévérité
8. Restitution         UI résultats (filtres, recherche, détail, preuve brute)
9. Export              JSON / HTML autonome / TXT, dans le dossier choisi par le staff
```

### Annulation

Un `CancellationTokenSource` unique, lié par module à un `CancellationTokenSource` de timeout.
Chaque boucle interne (records USN, fichiers `.pf`, régions mémoire) teste le jeton toutes les N itérations.
Une annulation **conserve les résultats partiels déjà obtenus** et permet quand même l'export, marqué
« scan interrompu ». Aucun handle laissé ouvert : tous les handles Win32 passent par `SafeHandle`.

### Progression

Poids relatifs déclarés par module (`Weight`), progression globale = somme pondérée des avancements.
L'UI affiche : pourcentage global, étape en cours, temps écoulé, éléments analysés (compteur cumulé),
indicateurs trouvés (compteur provisoire, recalculé à l'agrégation finale).

---

## 6. Journalisation (§24)

Deux journaux distincts :

- **Journal d'exécution** (`scan-YYYYMMDD-HHMMSS.log`) — horodaté, une ligne par événement notable :
  `[14:32:04] [usn] ✓ C: — 184 213 enregistrements lus, fenêtre 2026-08-14 → 2026-08-28 (2 641 ms)`
- **Journal d'accès** — chaque ressource **effectivement lue** (clé de registre, chemin, PID).
  C'est l'exigence de transparence du §1 : le staff, comme la personne analysée, peut vérifier a posteriori
  exactement ce que le programme a touché. Il est inclus dans le rapport JSON.

Niveaux : `Trace / Debug / Info / Warn / Error`. Aucune donnée sensible n'est journalisée (jamais de contenu
de credential, jamais de contenu de fichier).

---

## 7. Configuration

`data/config.default.json`, surchargeable par `%ProgramData%\GModForensicScanner\config.json`,
et par les options de l'écran d'accueil :

```jsonc
{
  "scan": {
    "moduleTimeoutSeconds": 180,
    "maxParallelModules": 4,
    "maxFileSizeForHashMb": 512
  },
  "filesystem": {
    "roots": ["%USERPROFILE%\\Downloads", "%USERPROFILE%\\Desktop", "%USERPROFILE%\\Documents",
              "%LOCALAPPDATA%\\Temp", "%APPDATA%", "<SteamLibraries>/GarrysMod"],
    "extensions": [".exe", ".dll", ".bat", ".cmd", ".ps1", ".jar", ".lua", ".gma",
                   ".zip", ".rar", ".7z"],
    "maxDepth": 6,
    "excludes": ["**\\node_modules\\**", "C:\\Windows\\WinSxS\\**", "**\\.git\\**"]
  },
  "usn": { "volumes": ["auto"], "maxRecords": 2000000 },
  "memory": { "targets": ["explorer.exe", "dllhost.exe", "svchost:EventLog",
                          "svchost:MpsSvc", "svchost:DPS"],
              "maxBytesPerProcessMb": 512, "minStringLength": 6 }
}
```

Les listes de connaissance (`data/rules/*.json`) sont **externes au binaire** et rechargeables sans
recompilation, conformément au §22.

---

## 8. Modèle de menace du scanner lui-même (§27)

| Risque | Mitigation |
|---|---|
| Exécution accidentelle d'un fichier découvert | `BannedApiAnalyzers` interdit `System.Diagnostics.Process` ; test post-build sur le binaire |
| Chargement d'une DLL découverte | Aucun `Assembly.Load*` ni `LoadLibrary` ; interdits par analyseur |
| Modification du système analysé | Toute lecture via `SafeFileReader` (read-only) ; registre ouvert en `RegistryRights.ReadKey` uniquement ; écriture limitée à `ReportOutputWriter` sous le dossier de sortie |
| Verrouillage d'un fichier en cours d'usage | `FileShare.ReadWrite | Delete` systématique |
| XSS dans le rapport HTML via un nom de fichier hostile (L11) | Échappement HTML de **tous** les champs, sans exception ; CSP restrictive dans le template ; aucun `innerHTML` |
| Fuite de secret | Le `CredentialBlob` n'est jamais marshalé (voir module OINK) ; aucun accès à Local Storage, cookies, ou bases de mots de passe |
| Exfiltration | Aucun accès réseau dans le produit ; aucune dépendance CDN dans le rapport HTML |

---

## 9. Étape suivante

`docs/03-modules.md` — implémentation détaillée des 17 modules, avec pour chacun l'analyse demandée au §30 :
ce qu'il détecte réellement, ses limites, l'API Windows employée, la méthode plus fiable retenue, et les
faux positifs connus.
