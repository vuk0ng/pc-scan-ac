# GMod Forensic Scanner

Outil d'aide à l'analyse forensic Windows, destiné au staff, pour retrouver rapidement des traces qui
seraient longues à rechercher manuellement avec Process Hacker, Regedit, PowerShell et CMD.

> **Ce logiciel est un scanner passif, en lecture seule.** Il recense des **indicateurs**.
> Aucun élément qu'il produit ne constitue à lui seul une preuve d'utilisation de cheat.

## État du projet

**Phase de conception — étapes 1 et 2 du plan.** Aucun code applicatif n'est encore écrit, conformément à
la méthode demandée (§29 : concevoir avant de coder).

| Document | Contenu |
|---|---|
| [`docs/01-analyse-et-choix-technologique.md`](docs/01-analyse-et-choix-technologique.md) | Analyse du besoin, 12 limites techniques structurantes, comparaison C# / C++ / Rust et décision |
| [`docs/02-architecture.md`](docs/02-architecture.md) | Arborescence, contrats de données, cycle de vie du scan, modèle de menace |
| [`docs/03-modules.md`](docs/03-modules.md) | Les 18 modules, chacun avec l'analyse exigée au §30 |
| [`docs/04-detection-scoring.md`](docs/04-detection-scoring.md) | Corrélation, règles, scoring contextuel, maîtrise des faux positifs |
| [`docs/05-ui-et-rapport.md`](docs/05-ui-et-rapport.md) | Interface WPF, rapports HTML/JSON/TXT, journalisation, erreurs, performance |
| [`docs/06-plan-developpement.md`](docs/06-plan-developpement.md) | Étapes 3 à 10, ordre d'implémentation, compilation, décisions ouvertes |

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
