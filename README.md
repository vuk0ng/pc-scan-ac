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
Étape 5     Les 18 modules réels                                à venir
```

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
dotnet test  GModForensicScanner.sln     # 59 tests

# Voir un scan se dérouler, puis ce qu'affiche l'écran de résultats :
dotnet test --filter Walkthrough --logger "console;verbosity=detailed"
```

## Structure

```
src/
├─ GModForensic.Abstractions   contrats purs (net8.0) — aucune dépendance Win32
├─ GModForensic.Native         P/Invoke générés par CsWin32 (net8.0-windows, x64)
├─ GModForensic.Scanners       les modules de collecte (net8.0-windows, x64)
├─ GModForensic.Detection      corrélation, règles, scoring (net8.0)
├─ GModForensic.Engine         orchestrateur, isolation, progression (net8.0)
├─ GModForensic.Reporting      JSON / HTML / TXT + unique point d'écriture (net8.0)
├─ GModForensic.Presentation   ViewModels MVVM, sans aucune référence WPF (net8.0)
└─ GModForensic.App            XAML, thème, câblage Windows (net8.0-windows, x64)
tests/GModForensic.Tests       xUnit (net8.0) — exécutable hors Windows
```
