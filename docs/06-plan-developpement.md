# Étapes 3 à 10 — Plan de développement

Méthode imposée par le §29 : progression par étapes, **code complet et compilable à chaque étape**,
jamais de pseudo-code. Chaque étape se termine par un livrable exécutable ou testable.

---

| Étape | Livrable | Critère de fin |
|---|---|---|
| **3 · Squelette** | Solution, 7 projets, `Directory.Build.props`, `BannedSymbols.txt`, `app.manifest`, contrats de `Abstractions`, `ScanOrchestrator` + `ModuleHost`, un module factice | `dotnet build` sans avertissement ; un scan factice s'exécute, progresse et s'annule proprement |
| **4 · Interface** | 4 écrans WPF, MVVM, thème sombre, progression, annulation, écran de résultats branché sur des données de démonstration | L'application se lance élevée, l'UI reste réactive, l'annulation fonctionne |
| **5 · Modules** | Les 18 modules, un par un, dans l'ordre ci-dessous | Chaque module a ses tests ; un module en échec n'interrompt jamais le scan |
| **6 · Moteur** | Normalisation, corrélation, chargeur de règles JSON, règles composites | Les scénarios de test produisent les entités attendues |
| **7 · Scoring** | `ScoreAggregator`, `ScoreBreakdown`, atténuateurs, `KnowledgeBase` | Suite de non-régression faux positifs : score < 20 sur les 4 profils de machine saine |
| **8 · Rapports** | JSON, HTML autonome, TXT | Le rapport s'ouvre hors ligne ; test d'échappement XSS au vert |
| **9 · Tests** | Couverture des parseurs sur échantillons binaires figés, tests de sûreté | Tests verts, aucune API interdite dans le binaire final |
| **10 · Distribution** | `GModForensicScanner.exe` unique, élevé, signé si possible | Lancement sur une machine propre Windows 10 et 11, sans .NET préinstallé |

### Ordre d'implémentation des modules (étape 5)

Du plus sûr et rentable au plus délicat — chaque groupe apporte une valeur utilisable même si le suivant
n'est pas terminé :

1. **Registre et fichiers plats** (risque faible, valeur immédiate) — M01 SystemInfo, M08 RegistryExecution,
   M09 BAM, M11 ArchiveHistory, M17 OINK.
2. **Processus** — M02 Process, M03 ProcessLifetime.
3. **Formats binaires** (les parseurs les plus techniques, testés sur échantillons figés) —
   M07 Prefetch, M10 RecentFiles (LNK).
4. **Volume NTFS** — M05 USN, puis M06 DeletedFiles qui l'agrège.
5. **Système de fichiers et fichiers** — M14 FileSystem, M15 ZoneIdentifier.
6. **Périphériques et journaux** — M12 USB, M13 EventLog.
7. **Sensibles en dernier** — M16 Discord (périmètre strict), M04 ProcessMemory (le plus bruyant),
   M18 AntiForensic (dépend des autres).

### Compilation (étape 10)

```
dotnet publish src/GModForensic.App/GModForensic.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=false
```

`EnableCompressionInSingleFile=false` est volontaire : la compression d'un exécutable unique augmente
nettement le taux de faux positifs antivirus (limite **L9**), or l'outil fait déjà tout ce qu'un
infostealer fait.

`app.manifest` : `requireAdministrator`, `longPathAware`, `dpiAwareness = PerMonitorV2`,
`supportedOS` Windows 10 et 11.

### Distribution

- Publier le **SHA-256** du binaire à chaque version, et de préférence signer avec un certificat de
  signature de code : sans cela, SmartScreen affichera un avertissement et l'outil de contrôle inspirera
  moins confiance que ce qu'il analyse.
- Fournir la note « pourquoi votre antivirus peut réagir » (L9), avec la liste des comportements
  légitimes en cause : énumération de processus, `ReadProcessMemory`, ouverture du volume brut.
- Ne jamais recommander de désactiver l'antivirus pour exécuter l'outil — cela contredirait directement
  la règle absolue du §1 et le §27.

---

## Ce que l'outil ne fera pas, et qu'il faut annoncer

Pour que le staff s'en serve correctement, ces limites doivent figurer dans le produit **et** dans le rapport :

- il **ne détecte pas** un cheat kernel bien conçu, un DMA, un second PC, ni un cheat exécuté depuis un
  support amovible retiré et jamais rebranché ;
- il **ne prouve rien** : il rassemble des indicateurs et les remet dans une chronologie ;
- une absence d'indicateur **n'est pas une innocence**, en particulier hors de la fenêtre couverte par le
  journal USN ;
- il est **contournable** par quelqu'un de compétent qui prépare sa machine à l'avance — mais dans ce cas,
  le nettoyage lui-même laisse des traces, ce qui est précisément l'objet du module M18.

---

## Décisions ouvertes

Ces choix relèvent de l'usage et méritent votre arbitrage avant l'étape 5 :

1. **Analyse mémoire (M04)** — profil `Standard` par défaut : je propose de la placer uniquement dans le
   profil `Approfondi`, car c'est le module le plus lent et le plus bruyant. Vous pouvez préférer l'inverse.
2. **Manifeste** — `requireAdministrator` strict (conforme au §2, mais aucun démarrage possible si l'UAC est
   refusé) ou `asInvoker` + auto-relance élevée (permet le mode dégradé annoncé au §2). Je peux livrer les
   deux comme configurations de build.
3. **Périmètre Discord (M16)** — la lecture des clés d'URL du cache HTTP est-elle acceptable dans votre
   cadre, ou faut-il s'en tenir à M15 (Zone.Identifier), qui donne un résultat comparable sans toucher aux
   données de Discord ? Le second est plus défendable si un joueur conteste le contrôle.
4. **Listes de connaissance** — disposez-vous de hashes ou de noms de cheats GMod avérés pour alimenter
   `blacklist.json` ? C'est la seule source pouvant produire une détection `Critique` fiable.
