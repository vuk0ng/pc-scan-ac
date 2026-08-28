# Étape 1 — Analyse du cahier des charges, limites techniques et choix technologique

> Statut : document de conception. Aucun code applicatif n'est encore écrit.

---

## 1. Ce que le projet est réellement

Ce n'est **pas** un anticheat. C'est un **collecteur d'artefacts forensic Windows + moteur de corrélation**,
dont la sortie est un dossier de preuves lisible par un humain.

Conséquence directe sur la conception : la valeur du logiciel ne vient **pas** du nombre de choses qu'il
détecte, mais de sa capacité à **ne pas noyer le staff sous des faux positifs**. Une machine Windows saine
contient déjà, en permanence :

- des dizaines de milliers d'entrées USN par jour ;
- des centaines d'entrées Prefetch ;
- la chaîne `C:\Users\` des milliers de fois dans la mémoire d'`explorer.exe` ;
- des `.exe` renommés depuis `.tmp` (c'est le mécanisme de mise à jour normal de Chrome, Discord, Steam) ;
- des DLL non signées (mods, plugins, outils de dev, runtimes).

Le cahier des charges le pressent (§22), mais il faut le poser comme **contrainte d'architecture n°1**, pas
comme une fonctionnalité optionnelle.

### Conséquence : séparation Observation / Détection

Le cahier des charges (§4) propose que chaque module retourne directement des `Detection`. C'est un piège :
chaque module inventerait alors son propre score, et le scoring contextuel demandé au §21 (« un `.exe`
supprimé **+** un ancien nom suspect **+** une trace Prefetch ») deviendrait impossible, puisque ces quatre
faits proviennent de quatre modules différents.

L'architecture retenue introduit donc une couche intermédiaire :

```
Modules  ──►  Observation[]   (des FAITS, non jugés, non scorés)
                    │
                    ▼
            Normalisation  (FileKey : chemin normalisé, nom, hash, ref MFT)
                    │
                    ▼
            Corrélation    (regroupement multi-sources par entité)
                    │
                    ▼
            Moteur de règles  ──►  Detection[]  (des JUGEMENTS, scorés, explicables)
```

Un module ne dit jamais « suspect ». Il dit « le fichier X a été renommé le T, source USN ». C'est le moteur,
qui voit toutes les sources en même temps, qui décide.

---

## 2. Limites techniques à connaître AVANT de coder

Ces limites ne sont pas des détails d'implémentation : elles déterminent ce que le produit peut honnêtement
promettre.

| # | Limite | Impact | Traitement retenu |
|---|--------|--------|-------------------|
| L1 | **Le journal USN est circulaire** (≈32 Mo par défaut sur C:, soit quelques jours à quelques semaines sur une machine active) | Un cheat utilisé il y a 3 semaines peut avoir totalement disparu du journal | Afficher explicitement la **fenêtre temporelle réellement couverte** (premier et dernier USN datés). Une absence de trace n'est jamais une preuve d'innocence, et le rapport doit le dire. |
| L2 | **Le journal USN peut être supprimé et recréé** (`fsutil usn deletejournal`) en quelques secondes | Efface la principale source | Lire le `JournalID` et la date du premier enregistrement : un journal **créé récemment** est en soi un indicateur anti-forensic fort. |
| L3 | **Prefetch peut être désactivé** (`EnablePrefetcher = 0`), ou vidé manuellement | Perte de la meilleure source « exécuté puis supprimé » | Vérifier la clé de configuration + comparer le nombre de `.pf` à l'uptime et à l'âge de l'installation. Un dossier Prefetch quasi vide sur une machine ancienne est un indicateur. |
| L4 | **BAM est purgé** (≈7 jours) et son emplacement varie selon les builds Windows | Fenêtre courte | Traiter comme une source d'appoint, jamais comme source unique. |
| L5 | **`Security` 4688 (création de processus) est désactivé par défaut** | La source la plus directe est presque toujours vide | Ne pas construire de détection dessus ; l'utiliser en bonus si présente. |
| L6 | **Certains processus sont protégés (PPL) ou inaccessibles**, même en administrateur | `ReadProcessMemory` échoue sur une partie des cibles | Statut par cible : `✓ / ⚠ / ✕`, jamais une erreur silencieuse. |
| L7 | **`EventLog`, `MpsSvc`, `DPS` ne sont pas des processus** mais des services hébergés dans un `svchost.exe` partagé | Une recherche naïve « processus nommé EventLog » ne trouve rien | Résolution service → PID via le SCM (`QueryServiceStatusEx`), puis analyse du `svchost` hôte, en indiquant quels services y cohabitent. |
| L8 | **Une chaîne en mémoire n'est presque jamais un indicateur exploitable brut** | `.exe`, `.dll`, `C:\Users\` sont présents des milliers de fois légitimement | Voir §3 ci-dessous : la recherche de chaînes est refondue en extraction de **chemins**, avec filtrage par existence sur disque et corrélation. |
| L9 | **Un scanner qui énumère les processus, lit leur mémoire et ouvre le volume brut ressemble exactement à un infostealer** | Detection heuristique par Defender/EDR très probable | Documenter, signer le binaire, publier les hashes ; ne pas packer/obfusquer (ce qui aggraverait le problème). |
| L10 | **Aucune de ces techniques ne détecte un cheat kernel bien fait, un DMA, ou un second PC** | Le périmètre est borné | L'assumer dans le rapport : ce logiciel accélère une recherche d'artefacts, il ne rend pas de verdict. |
| L11 | **Les noms de fichiers sont contrôlés par la personne analysée** | Un fichier nommé `<img src=x onerror=...>.exe` casse ou compromet le rapport HTML | Échappement HTML strict et systématique dans le générateur de rapport (traiter chaque champ comme hostile). |
| L12 | **`requireAdministrator` empêche le démarrage si l'UAC est refusé** | Le §2 demande « si le lancement échoue, afficher ce qui ne pourra pas être fait » — impossible si le process ne démarre pas | Manifeste `requireAdministrator` comme demandé, **plus** une détection runtime des privilèges réellement obtenus, **plus** une variante de build `asInvoker` + auto-relance qui, en cas de refus, démarre en mode dégradé avec la liste des modules indisponibles. |

---

## 3. Reprise critique du §7 (analyse mémoire passive)

Le cahier des charges demande de chercher en mémoire :

```
file:///   .exe   .dll   .bat   .jar   .lua   .exe.config
Zone.Identifier   /C:/   C:\Users\   \Users\
\Device\HarddiskVolume   MonitorProcess   UDP Query
```

**Analyse honnête : appliqué tel quel, ce filtre produit des milliers de résultats sur une machine saine.**
`explorer.exe` contient en permanence des centaines de chemins `C:\Users\`, `\Device\HarddiskVolume` est la
forme normale de tout chemin NT interne, et `.exe` apparaît dans chaque entrée de menu Démarrer.

Ce que la technique manuelle détecte **réellement** entre les mains d'un staff expérimenté : ce n'est pas la
présence de ces sous-chaînes, c'est le fait qu'un **chemin complet vers un fichier qui n'existe plus** subsiste
dans la mémoire du shell — parce qu'`explorer.exe` a affiché, lancé ou copié ce fichier avant sa suppression.

Le module est donc conçu ainsi :

1. Extraire les chaînes ASCII/UTF-16 des régions `MEM_COMMIT` lisibles ;
2. **Ne conserver que celles qui correspondent à une forme de chemin** :
   `[A-Za-z]:\\…`, `\\Device\\HarddiskVolume…`, `file:///…`, `\\\\?\\…`, UNC ;
3. Normaliser (`\Device\HarddiskVolumeN` → lettre de volume via `QueryDosDevice`) ;
4. **Filtrer par existence** : un chemin pointant vers un fichier toujours présent et signé est du bruit ;
5. Ne remonter comme observation que : *chemin absent du disque* **ou** *situé dans Downloads/Temp/AppData*
   **ou** *extension exécutable* ;
6. Le reste part dans la section « données brutes » du rapport (consultable, non scoré).

Les motifs `MonitorProcess` et `UDP Query` sont conservés tels quels comme recherche littérale distincte
(ce sont des chaînes spécifiques, pas génériques), avec une confiance faible par défaut.

**Aucune écriture mémoire. `PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION` uniquement.**

---

## 4. Choix technologique

### 4.1 Comparaison

| Critère | C# / .NET 8 | C++ (Win32) | Rust |
|---|---|---|---|
| **Accès API Windows** | Très bon — `CsWin32` génère les P/Invoke depuis les métadonnées officielles Win32 ; registre, WMI/CIM, Event Log et Authenticode sont dans la BCL | Optimal, natif, zéro marshalling | Très bon — `windows-rs`, bindings officiels Microsoft |
| **Stabilité sur parsing binaire hostile** (USN, Prefetch, LNK, ruches registre) | Élevée — bornes vérifiées, une exception reste une exception | **Faible** — c'est exactement le terrain des dépassements de tampon et des UAF | Maximale |
| **Isolation des pannes par module** (§25) | Native (try/catch, pas de corruption d'état global) | Un module qui corrompt le tas emporte tout le process | Native |
| **Performance** | Suffisante — la charge est I/O disque, pas CPU ; le marshalling d'un buffer USN de 1 Mo est négligeable | Meilleure | Meilleure |
| **Vitesse de développement** | Élevée | Faible (×3 à ×5) | Moyenne (courbe d'apprentissage, écosystème forensic plus mince) |
| **UI moderne et professionnelle** | WPF : mature, MVVM, thèmes, virtualisation de listes de 100 000 lignes | WinUI 3 en C++ pénible ; Qt = licence + déploiement | Point faible : `egui`/`iced` non natifs, Tauri impose WebView2 |
| **Compilation en `.exe` unique** | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` | Natif | Natif |
| **Privilèges administrateur** | `app.manifest` intégré par le SDK | Natif | Natif |
| **Maintenance / reprise par un tiers** | Excellente | Faible | Moyenne |

### 4.2 Décision : **C# / .NET 8 + WPF, cible `win-x64`**

Raisonnement en une phrase : le cœur du travail est du **parsing de structures Windows + de l'agrégation +
de l'UI**, pas du calcul intensif ; C# fournit ~90 % de l'accès système du C++ pour une fraction du risque,
et la contrainte la plus dure du cahier des charges (§25 : *« ne doit jamais planter parce qu'un fichier est
inaccessible »*) est structurellement plus facile à garantir en code managé.

Rust serait le meilleur choix pour le seul cœur de parsing ; il perd sur l'UI, qui représente ici une part
importante du produit. C++ est écarté : écrire des parseurs de formats binaires non fiables en C++ sur un
outil manipulé par du personnel non-développeur est un mauvais compromis risque/bénéfice.

### 4.3 Pile technique retenue

| Élément | Choix | Justification |
|---|---|---|
| Runtime | .NET 8 (LTS), `net8.0-windows`, `x64` **obligatoire** | x86 ne peut pas lire la mémoire d'un processus 64 bits |
| UI | WPF + MVVM (`CommunityToolkit.Mvvm`) | Mature, virtualisation, thème sombre personnalisable |
| Interop | `Microsoft.Windows.CsWin32` (générateur de source) | Signatures P/Invoke correctes, générées, pas écrites à la main |
| Registre | `Microsoft.Win32.Registry` | Ouverture **en lecture seule exclusivement** |
| Event Log | `System.Diagnostics.Eventing.Reader` | API `EvtQuery`, pas de `wevtutil.exe` |
| WMI/CIM | `System.Management` | Ligne de commande + date de démarrage des processus en une requête |
| JSON | `System.Text.Json` (source-generated) | Pas de dépendance externe, rapide |
| Tests | xUnit + FluentAssertions | Parseurs testés sur échantillons binaires figés |
| Garde-fou | `Microsoft.CodeAnalysis.BannedApiAnalyzers` | **Voir 4.4** |
| Packaging | `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` | Un seul `GModForensicScanner.exe` |

### 4.4 La RÈGLE ABSOLUE devient une contrainte de compilation

Le point le plus important de tout le cahier des charges (« le programme ne doit JAMAIS exécuter, charger,
modifier, supprimer… ») ne doit pas reposer sur la discipline du développeur. Il est transformé en **erreur
de compilation** via `BannedApiAnalyzers` et un fichier `BannedSymbols.txt` :

```
T:System.Diagnostics.Process;                       Interdit : exécution de processus (RÈGLE ABSOLUE)
M:System.Reflection.Assembly.LoadFrom(System.String); Interdit : chargement de code
M:System.Reflection.Assembly.LoadFile(System.String); Interdit : chargement de code
M:System.IO.File.Delete(System.String);             Interdit : suppression
M:System.IO.File.Move(System.String,System.String);  Interdit : renommage
M:System.IO.File.WriteAllText(System.String,System.String); Utiliser ReportWriter (sortie contrôlée)
M:System.IO.File.Create(System.String);             Utiliser ReportWriter
M:Microsoft.Win32.RegistryKey.SetValue(System.String,System.Object); Interdit : écriture registre
M:Microsoft.Win32.RegistryKey.DeleteValue(System.String);            Interdit : écriture registre
M:Microsoft.Win32.RegistryKey.CreateSubKey(System.String);           Interdit : écriture registre
```

Les rares écritures légitimes (rapport, journal applicatif) passent par un composant unique
`ReportOutputWriter`, seul autorisé à écrire, et seulement sous le répertoire de sortie choisi par le staff.
De même, **toute** lecture de fichier passe par `SafeFileReader`, qui ouvre exclusivement en
`FileMode.Open | FileAccess.Read | FileShare.ReadWrite|Delete` — jamais autre chose.

Un test unitaire complémentaire parcourt les assemblies compilées et échoue si un appel à
`LoadLibrary`, `CreateProcess`, `WriteProcessMemory` ou `CreateRemoteThread` apparaît dans le binaire final.

**Résultat : la garantie « scanner uniquement » est vérifiable mécaniquement, pas seulement affirmée.**

### 4.5 Privilèges demandés et pourquoi

| Privilège | Nécessaire pour | Nature |
|---|---|---|
| Groupe `Administrators` (token élevé) | Ouvrir `\\.\C:` (USN), lire `HKLM\SYSTEM\...\bam`, lire `C:\Windows\Prefetch` | Élévation UAC standard |
| `SeDebugPrivilege` | Ouvrir les processus d'autres utilisateurs en `PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ` | Activation d'un privilège **déjà présent** dans le token élevé, via `AdjustTokenPrivileges` — ce n'est pas un contournement, c'est l'usage documenté |
| `SeSecurityPrivilege` | Lire le canal `Security` de l'Event Log | Idem |

Aucun autre privilège n'est activé. Aucune tentative de contournement d'UAC, de PPL, ou d'un antivirus.

---

## 5. Cadre d'usage (à afficher dans le produit)

L'outil lit des artefacts système détaillés sur une machine personnelle. Il doit donc :

- **exiger un consentement explicite** de la personne analysée avant le scan (case à cocher + nom du staff
  et nom du sujet enregistrés dans le rapport) ;
- **minimiser** : aucun mot de passe, aucun token, aucun cookie, aucun message privé, aucun contenu de
  document ; uniquement des métadonnées et des noms d'artefacts ;
- **être transparent** : tout ce qui est collecté apparaît dans le rapport, sans collecte cachée ;
- **rester local** : aucune transmission réseau, aucune télémétrie. Le rapport est un fichier, le staff décide
  de son sort.

Le rapport porte en en-tête une clause fixe : *« Ce document recense des indicateurs. Aucun élément ci-dessous
ne constitue à lui seul une preuve d'utilisation de cheat. »*

---

## 6. Prochaine étape

`docs/02-architecture.md` — arborescence complète, contrats de données, cycle de vie du scan.
