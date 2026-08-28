# GMod Forensic Scanner

Outil d'aide à l'analyse forensic Windows, destiné au staff, pour retrouver rapidement des traces qui
seraient longues à rechercher manuellement avec Process Hacker, Regedit, PowerShell et CMD.

> **Ce logiciel est un scanner passif, en lecture seule.** Il recense des **indicateurs**.
> Aucun élément qu'il produit ne constitue à lui seul une preuve d'utilisation de cheat.

## État du projet

**Étape 4 terminée — interface complète.** 4 écrans WPF en MVVM, thème sombre, export JSON et TXT.
La solution compile sans aucun avertissement et 59 tests passent. Les modules réels arrivent à
l'étape 5 ; ceux du catalogue actuel sont des modules de démonstration.

```
Étapes 1–2  Analyse, choix technologique, architecture          ✓ livré
Étape 3     Squelette : garde-fous, orchestrateur               ✓ livré
Étape 4     Interface : 4 écrans, MVVM, scoring, export         ✓ livré
Étape 5     Les modules réels                                   5 / 18
```

**Modules réels livrés** : informations système · registre (AppCompatFlags, MuiCache,
FeatureUsage, UserAssist) · BAM · historique d'archives (WinRAR, 7-Zip) · identifiants (OINK).
Les autres sont encore représentés par un module de démonstration, dont le libellé le dit
explicitement — un rapport ne doit jamais laisser croire à une vérification qui n'a pas eu lieu.

> L'interface n'a pas pu être vue : WPF ne s'exécute pas sur Linux. Le XAML compile et toute la
> logique est testée, mais le rendu visuel reste à vérifier sur une machine Windows.

| Document | Contenu |
|---|---|
| [`docs/01-analyse-et-choix-technologique.md`](docs/01-analyse-et-choix-technologique.md) | Analyse du besoin, 12 limites techniques structurantes, comparaison C# / C++ / Rust et décision |
| [`docs/02-architecture.md`](docs/02-architecture.md) | Arborescence, contrats de données, cycle de vie du scan, modèle de menace |
| [`docs/03-modules.md`](docs/03-modules.md) | Les 18 modules, chacun avec l'analyse exigée au §30 |
| [`docs/04-detection-scoring.md`](docs/04-detection-scoring.md) | Corrélation, règles, scoring contextuel, maîtrise des faux positifs |
| [`docs/05-ui-et-rapport.md`](docs/05-ui-et-rapport.md) | Interface WPF, rapports HTML/JSON/TXT, journalisation, erreurs, performance |
| [`docs/06-plan-developpement.md`](docs/06-plan-developpement.md) | Étapes 3 à 10, ordre d'implémentation, compilation, décisions ouvertes |
| [`docs/07-etape-3-squelette.md`](docs/07-etape-3-squelette.md) | Ce que contient le squelette, comment le compiler, ce que les tests prouvent |
| [`docs/08-etape-4-interface.md`](docs/08-etape-4-interface.md) | Les 4 écrans, la séparation qui rend l'interface testable, les limites de vérification |

## Technologie retenue

**C# / .NET 8 + WPF, cible `win-x64`**, avec `CsWin32` pour les liaisons Win32 générées.
Justification complète dans `docs/01`, section 4.

## La règle absolue, appliquée à la compilation

Le programme **n'exécute jamais** un fichier découvert, **ne charge jamais** une DLL trouvée, **n'injecte
rien**, **ne modifie ni ne supprime ni ne renomme** quoi que ce soit, **n'écrit jamais** dans le registre,
**ne touche pas** aux paramètres de sécurité, et **ne récupère aucun mot de passe ni secret**.

Cette garantie ne repose pas sur la discipline du développeur : `BannedApiAnalyzers` transforme l'usage de
`System.Diagnostics.Process`, `Assembly.LoadFrom`, `File.Delete`, `File.Move`, `RegistryKey.SetValue` et
consorts en **erreur de compilation**, et un test post-build vérifie l'absence de `LoadLibrary`,
`CreateProcess`, `WriteProcessMemory` et `CreateRemoteThread` dans le binaire final.

## Cadre d'usage

Consentement explicite de la personne analysée, collecte minimisée (aucun mot de passe, token, cookie ou
message privé), traitement entièrement local, aucune télémétrie, et transparence totale : tout ce qui est
collecté figure dans le rapport, accompagné du journal des ressources réellement lues.

## Compiler et tester

Prérequis : SDK .NET 8. La solution se compile depuis Windows, Linux ou macOS
(`EnableWindowsTargeting` permet de produire le binaire Windows depuis la CI) ; seul le lancement
de `GModForensicScanner.exe` requiert Windows.

```bash
dotnet build GModForensicScanner.sln     # doit rester à 0 avertissement
dotnet test  GModForensicScanner.sln     # 90 tests

# Voir un scan se dérouler, puis ce qu'affiche l'écran de résultats :
dotnet test --filter Walkthrough --logger "console;verbosity=detailed"
```

## Télécharger et exécuter

> ### ⚠️ À l'étape 4, l'exécutable ne scanne encore RIEN
> Le catalogue ne contient que des **modules de démonstration** : ils simulent une charge, affichent
> une progression et produisent des détections d'exemple préfixées `DEMO.`. Aucun artefact Windows
> n'est lu. L'application sert à valider l'interface et l'orchestration — **elle n'est pas utilisable
> pour un contrôle réel** tant que l'étape 5 n'est pas livrée.

### Télécharger

**[Dernière version publiée](https://github.com/vuk0ng/pc-scan-ac/releases/latest)** — lien direct,
aucun compte GitHub requis. Chaque release contient `GModForensicScanner.exe` et `SHA256.txt`.

Chaque push produit aussi un exécutable : onglet **Actions** → dernière exécution → artefact
`GModForensicScanner-win-x64`. Celui-ci exige en revanche d'être **connecté à GitHub**, et expire
au bout de 30 jours.

Pour publier une nouvelle version : onglet **Actions** → workflow **build** → *Run workflow* →
renseigner `release_tag` (par exemple `v0.5.0`). Un tag comportant un tiret part en pre-release.
Un push de tag `vX.Y.Z` fonctionne également.

### Ou compiler soi-même

Prérequis : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/vuk0ng/pc-scan-ac.git
cd pc-scan-ac
dotnet publish src/GModForensic.App/GModForensic.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false `
  -o publish
```

L'exécutable est dans `publish\GModForensicScanner.exe` (≈ 154 Mo, autonome : aucun runtime .NET à
installer sur la machine analysée).

### Exécuter

Windows 10 ou 11 **64 bits**. Double-cliquer sur l'exécutable.

1. **UAC** — le manifeste demande `requireAdministrator`. Un refus empêche le démarrage : c'est
   volontaire, l'UAC n'est jamais contourné.
2. **SmartScreen** — l'exécutable n'est pas signé : « Windows a protégé votre ordinateur » →
   *Informations complémentaires* → *Exécuter quand même*. Vérifiez l'empreinte SHA-256 avant.
3. **Antivirus** — Defender ou un EDR peut réagir. C'est attendu : énumérer les processus, lire leur
   mémoire et ouvrir le volume brut est exactement ce que fait un infostealer. **Ne désactivez jamais
   votre antivirus pour l'exécuter** — cela contredirait la raison d'être de l'outil.

Vérifier l'empreinte avant exécution :

```powershell
Get-FileHash .\GModForensicScanner.exe -Algorithm SHA256
```

L'application ne fait aucun accès réseau et n'envoie aucune donnée. Le rapport est un fichier local,
écrit uniquement dans le dossier que vous choisissez.

## Lire un rapport

`tools/report-reader.html` est un **lecteur autonome** : collez le contenu de `rapport.json`
(ou déposez le fichier) et il affiche un tableau de bord — score et sa décomposition par
catégorie, état des modules, indicateurs filtrables avec le détail du score et les causes
légitimes possibles, chronologie unifiée, données brutes et journaux.

Enregistrez le fichier et ouvrez-le par double-clic. Il fonctionne **hors ligne** : aucune
ressource externe, aucune requête réseau, aucune donnée transmise — la lecture se fait
entièrement dans le navigateur.

Les noms de fichiers d'un rapport proviennent de la machine analysée : ce sont des données
hostiles. Le lecteur les traite exclusivement comme du texte (`textContent` / `createElement`),
et un test de la suite échoue si une API d'injection de balisage ou un accès réseau y apparaît.

Ce lecteur remplace avantageusement l'export HTML prévu au §23 : une seule page consomme
n'importe quel rapport, et elle se met à jour sans recompiler le scanner.

## Structure

```
src/
├─ GModForensic.Abstractions   contrats purs (net8.0) — aucune dépendance Win32
├─ GModForensic.Native         P/Invoke générés par CsWin32 (net8.0-windows, x64)
├─ GModForensic.Parsers       décodeurs purs des formats Windows (net8.0) — testables
├─ GModForensic.Scanners       les modules de collecte (net8.0-windows, x64)
├─ GModForensic.Detection      corrélation, règles, scoring (net8.0)
├─ GModForensic.Engine         orchestrateur, isolation, progression (net8.0)
├─ GModForensic.Reporting      JSON / HTML / TXT + unique point d'écriture (net8.0)
├─ GModForensic.Presentation   ViewModels MVVM, sans aucune référence WPF (net8.0)
└─ GModForensic.App            XAML, thème, câblage Windows (net8.0-windows, x64)
tests/GModForensic.Tests       xUnit (net8.0) — exécutable hors Windows
```
