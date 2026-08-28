# Étape 3 — Squelette du projet

Objectif de l'étape : une solution qui compile, un orchestrateur qui fonctionne, et les garde-fous en
place — **avant** d'écrire le premier module réel. Aucun artefact Windows n'est encore lu.

**Résultat** : `dotnet build` à 0 avertissement, `dotnet test` à 31 tests verts,
`GModForensicScanner.exe` produit.

---

## 1. La règle absolue est désormais une erreur de compilation

C'est le livrable le plus important de cette étape. `BannedApiAnalyzers` + `BannedSymbols.txt` +
`dotnet_diagnostic.RS0030.severity = error` transforment toute violation en échec de build.

Vérification faite en introduisant délibérément le code interdit :

```csharp
System.Diagnostics.Process.Start(path);
System.IO.File.Delete(path);
System.IO.File.Move(path, path + ".bak");
```

```
error RS0030: The symbol 'Process' is banned in this project:
  INTERDIT : le scanner n'execute jamais de processus.
  Utiliser NtQuerySystemInformation via GModForensic.Native pour ENUMERER sans lancer.
error RS0030: The symbol 'File.Delete(string)' is banned : INTERDIT : aucune suppression de fichier.
error RS0030: The symbol 'File.Move(string, string)' is banned : INTERDIT : aucun renommage de fichier.
Build FAILED.
```

Le message d'erreur indique la raison **et** l'alternative correcte, en français : un développeur qui
arrive sur le projet ne peut pas violer la règle par ignorance.

Trois protections complémentaires, testées :

| Protection | Mécanisme |
|---|---|
| Interdictions non retirables | Un test échoue si une entrée critique disparaît de `BannedSymbols.txt` |
| Sévérité non abaissable | Un test échoue si `RS0030` n'est plus en `error` |
| Dérogations concentrées | Un test échoue si un `#pragma warning disable RS0030` apparaît hors de `ReportOutputWriter.cs` |
| Pas d'API d'exécution ou d'injection | Un test balaie les sources et `NativeMethods.txt` (`CreateRemoteThread`, `WriteProcessMemory`, `LoadLibrary`, `CreateProcess`, `AdjustTokenPrivileges`…) |

Le contrôle au niveau de l'IL compilé reste prévu pour l'étape 9.

---

## 2. Les contrats

La séparation annoncée dans `docs/02` est en place et encodée dans les types :

- `Observation` — un **fait** produit par un module. Aucun score, aucun jugement.
- `Detection` — un **jugement**, que seul le moteur peut produire.
- `FileKey` — la clé de corrélation. `IsSameEntityAs` refuse de fusionner deux fichiers
  **sur la seule base du nom** : il faut un SHA-256, un chemin complet ou une référence MFT.
- `Evidence` — la trace brute et son `VerificationHint`, avec troncature dont la longueur d'origine
  reste visible.
- `Detection.FalsePositiveNote` est `required` : il est **impossible** de compiler une détection sans
  documenter ce qui pourrait l'expliquer légitimement. Le §22 est appliqué par le compilateur.

Le barème du §21 (`+5 / +15 / +30 / +50`) est en place, et un test reproduit l'exemple exact du cahier
des charges — 15 + 15 + 20 + 10 = **60/100**.

---

## 3. L'orchestrateur

`ScanOrchestrator` + `ModuleHost` tiennent la promesse du §25. Sept tests couvrent :

| Situation | Comportement vérifié |
|---|---|
| Module qui lève une exception | `✕ Failed` avec le motif — **le scan continue** |
| Capacité absente | `○ Skipped` avec le motif affichable (« journal Security inaccessible ») |
| Dépassement du délai | `⚠ Partial`, pas `Failed` — les résultats acquis restent valides |
| Annulation | `⊘ Cancelled`, résultats des modules déjà terminés **conservés** |
| Module désactivé par le staff | Ignoré sans être instancié |
| Progression | Pondérée par `Weight`, jamais décroissante, se termine toujours à 100 % |

### Deux défauts trouvés et corrigés pendant l'étape

Le déroulé complet (`--filter Walkthrough`) a mis au jour deux problèmes qu'aucune revue de code
n'aurait rendus évidents :

1. **Les compteurs d'un module interrompu étaient perdus.** Un module annulé lève avant d'avoir pu
   construire son résultat : son travail disparaissait du rapport alors qu'il avait bien eu lieu.
   L'orchestrateur reprend désormais le dernier avancement signalé.
   *Le walkthrough affichait « Éléments analysés : 0 » après avoir traité 15 éléments.*

2. **La barre de progression pouvait reculer.** Les notifications passent par `IProgress<T>` et sont
   donc **délivrées** dans le désordre quand plusieurs modules progressent en parallèle.
   `ScanProgressSnapshot` porte maintenant un numéro de séquence, l'avancement d'un module ne peut
   plus décroître, et l'interface ignore toute notification périmée.
   *Ce défaut se manifestait comme un test instable — 1 échec sur 3 exécutions.*

### Déroulé observé

```
SCANNER FORENSIC GMod

###################### 100 %
Étape courante    : Terminé
Éléments analysés : 15
Scan annulé       : OUI — résultats partiels conservés

  ✓  registre        6 éléments
  ⊘  prefetch        6 éléments  — Scan annulé.
  ○  eventlog        0 éléments  — journal Security inaccessible (SeSecurityPrivilege absent)
  ✕  usn             0 éléments  — UnauthorizedAccessException : volume verrouillé
  ⊘  memoire         3 éléments  — Scan annulé.
```

Les cinq états de premier plan du §25 apparaissent dans un même scan, et aucun n'interrompt les autres.

---

## 4. La couche native

`CsWin32` génère les signatures P/Invoke depuis les métadonnées officielles Win32 — elles ne sont
jamais écrites à la main. `NativeMethods.txt` est la **liste blanche** des API générées : il est
commenté, revu à chaque modification, et un test vérifie qu'aucune API d'écriture n'y figure.

Deux composants sont en place :

- **`SafeFileReader`** — point d'entrée unique de toute lecture de fichier, en
  `FileAccess.Read` + `FileShare.ReadWrite | Delete`. Les surcharges de `File.Open` étant bannies,
  aucune lecture ne peut contourner ces garanties : le fichier analysé n'est jamais verrouillé, et
  reste supprimable par son propriétaire pendant l'analyse.
- **`TokenInspector` / `CapabilityProbe`** — mesure des privilèges **réellement** obtenus.
  `PrivilegeCheck` n'est délibérément pas utilisé (cette API exige un jeton d'usurpation et échoue sur
  un jeton primaire) : `TokenPrivileges` est lu directement et les LUID comparés.

---

## 5. L'application

`app.manifest` est en place : `requireAdministrator`, `longPathAware`, `dpiAwareness PerMonitorV2`,
`activeCodePage UTF-8`, Windows 10 et 11. `GModForensicScanner.exe` est produit.

La fenêtre actuelle est une coquille minimale : elle affiche les privilèges mesurés, lance le scan de
démonstration, montre la progression et l'annule. **L'interface complète — 4 écrans, MVVM, thème — est
l'objet de l'étape 4** ; cette coquille existe pour prouver que l'orchestrateur pilote bien une UI.

---

## 6. Écarts assumés par rapport à `docs/02`

| Point | Décision |
|---|---|
| `GModForensic.Detection` | Le namespace masque le type `Detection` du modèle. Un alias `DetectionRecord` lève l'ambiguïté sans renommer ni l'un ni l'autre. |
| Parseurs binaires | `Native` et `Scanners` ciblent `net8.0-windows`, donc non testables hors Windows. À l'étape 5, les parseurs purs (Prefetch, LNK, enregistrements USN) seront extraits dans un projet `net8.0` pour être testables sur échantillons figés en CI. |
| `FluentAssertions` | Écarté : la version 8 est sous licence commerciale. Les assertions xUnit natives suffisent. |
| `CommunityToolkit.Mvvm` | Ajouté à l'étape 4, avec les ViewModels — pas de dépendance inutilisée maintenant. |

---

## 7. Ce que l'étape 3 ne fait pas

Aucun artefact Windows n'est lu. Le catalogue ne contient que des modules de démonstration, qui seront
supprimés à l'étape 5 : ils simulent une charge, signalent une progression et respectent l'annulation,
mais ne collectent rien. Le moteur de détection est un squelette dont le contrat est figé — la
corrélation arrive à l'étape 6, le scoring à l'étape 7.
