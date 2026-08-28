# Étape 2 (suite) — Implémentation détaillée de chaque module

Pour chaque module, l'analyse exigée au **§30** du cahier des charges :
**(1)** ce que la technique détecte réellement · **(2)** ses limites · **(3)** l'API Windows employée ·
**(4)** la méthode plus fiable retenue · **(5)** les faux positifs connus.

Rappel transversal : **aucun module ne produit de score.** Chacun produit des `Observation`.

---

## M01 · SystemInfoScanner

**Rôle.** Contexte du rapport : version et build Windows, uptime, date d'installation, SID et nom
d'utilisateur, fuseau et **décalage horloge**, volumes (système de fichiers, taille, numéro de série),
Secure Boot, état de Defender, présence d'un hyperviseur, mode test-signing.

**API.** `RtlGetVersion`, `GetTickCount64`, `GetLogicalDrives` + `GetVolumeInformation`,
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` (`InstallDate`, `BuildLabEx`),
WMI `MSFT_MpComputerStatus`, `BCD` via `SystemSecureBootEnabled`.

**Pourquoi c'est important.** `bcdedit /set testsigning on` et Secure Boot désactivé sont deux
prérequis fréquents au chargement d'un driver de cheat non signé. Ce sont des **observations de contexte
à forte valeur**, souvent oubliées.

**Faux positifs.** Test-signing est aussi activé par les développeurs de drivers et certains outils
matériels. Secure Boot désactivé est courant sur les machines à double amorçage Linux.

---

## M02 · ProcessScanner

**(1) Détecte réellement.** L'état instantané de la machine : ce qui tourne **au moment du contrôle**.
Rien d'autre. Un cheat externe fermé avant le contrôle est invisible ici.

**(2) Limites.** Un module injecté par *manual mapping* n'apparaît dans aucune liste de modules.
`Module32First` échoue depuis un processus 32 bits sur une cible 64 bits (d'où la cible `x64` obligatoire).
La ligne de commande peut être inaccessible sur les processus protégés.

**(3) API.**
- Énumération : `NtQuerySystemInformation(SystemProcessInformation)` — une seule passe, donne PID, PPID,
  heure de création, nombre de threads. Repli : `CreateToolhelp32Snapshot`.
- Chemin : `QueryFullProcessImageName` (fiable, y compris WOW64) — **pas** `Process.MainModule`.
- Ligne de commande et utilisateur : WMI `Win32_Process` (`CommandLine`, `GetOwner`) en une requête groupée,
  bien plus rapide qu'un `NtQueryInformationProcess(ProcessBasicInformation)` + lecture du PEB par processus.
- Modules chargés : `EnumProcessModulesEx(LIST_MODULES_ALL)`.
- Réseau : `GetExtendedTcpTable` / `GetExtendedUdpTable` (`TCP_TABLE_OWNER_PID_ALL`) → jointure par PID.
- Signature : `WinVerifyTrust` (`WINTRUST_ACTION_GENERIC_VERIFY_V2`) + catalogues système, puis lecture du
  sujet du certificat.
- Hash : SHA-256 en flux, fichier ouvert en lecture seule, plafonné à la taille configurée.

**(4) Méthode plus fiable retenue.** Croiser trois axes plutôt que lister : *processus sans chemin résoluble*,
*module chargé depuis un répertoire temporaire*, *exécutable non signé dont le nom imite un binaire système*
(`svch0st.exe`, `explorer .exe`, `dllhost.exe` hors de `System32`). La **discordance nom/chemin/éditeur** est
un signal bien plus utile que la simple absence de signature.

**(5) Faux positifs.** Très nombreux binaires légitimes non signés (mods, outils de dev, launchers de jeux,
logiciels de périphériques). Le module observe, il ne conclut pas.

**Interdits.** Jamais de `TerminateProcess`, `SuspendThread`, `OpenProcess(PROCESS_VM_WRITE)`.

---

## M03 · ProcessLifetimeScanner (explorer.exe — §6)

**(1) Détecte réellement.** Depuis combien de temps le shell tourne. L'hypothèse manuelle est qu'un
redémarrage d'`explorer.exe` juste avant le contrôle peut accompagner un nettoyage.

**(2) Limites.** **C'est l'indicateur le plus faible et le plus mal interprété du cahier des charges.**
`explorer.exe` redémarre pour de très nombreuses raisons légitimes : plantage du shell, changement de
résolution ou de DPI, mise à jour Windows, installation d'une extension shell, `Ctrl+Shift+Échap` →
redémarrage manuel, session RDP, changement de thème.

**(3) API.** `GetProcessTimes` → `CreationTime` exact (précision 100 ns), converti en UTC.
Nombre d'instances via l'énumération M02.

**(4) Méthode plus fiable retenue.** Ne pas s'arrêter à « il y a 2 minutes ». Corréler avec :
- Event Log `Application` : `Application Error` / `Windows Error Reporting` mentionnant `explorer.exe`
  → un redémarrage **expliqué par un plantage** est bénin ;
- `System` : événements d'arrêt/démarrage, changement de session ;
- l'uptime de la machine : si l'uptime est de 3 minutes, explorer a 3 minutes — c'est un non-événement ;
- l'écart entre le démarrage d'`explorer` et celui de `winlogon`/`userinit`.

Le module ne remonte l'observation que si **explorer est nettement plus jeune que la session utilisateur**.

**(5) Faux positifs.** Massifs. Le rapport affiche systématiquement :

```
explorer.exe — démarré il y a 2 min (session ouverte depuis 3 h 41)
État : INDICATEUR À VÉRIFIER · confiance FAIBLE
Un redémarrage récent d'Explorer a de nombreuses causes légitimes
(plantage du shell, changement de DPI, mise à jour, redémarrage manuel).
Aucun plantage correspondant trouvé dans le journal Application.
```

---

## M04 · ProcessMemoryScanner (§7)

Voir l'analyse détaillée dans `docs/01`, section 3. Résumé de l'implémentation :

**(3) API.** `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ)` →
`VirtualQueryEx` pour parcourir les régions → `ReadProcessMemory` sur les régions `MEM_COMMIT` dont la
protection est lisible (`PAGE_READONLY`, `PAGE_READWRITE`, `PAGE_WRITECOPY`, `PAGE_EXECUTE_READ*`),
en excluant `PAGE_GUARD` et `PAGE_NOACCESS`. Lecture par blocs de 1 Mo, plafond configurable par processus.

**Résolution des cibles service.** `EventLog`, `MpsSvc`, `DPS` sont des **services**, pas des processus :
`OpenSCManager` → `OpenService` → `QueryServiceStatusEx(SERVICE_STATUS_PROCESS)` → PID du `svchost` hôte.
Le rapport indique quels services cohabitent dans ce `svchost`, car une chaîne trouvée dedans ne peut pas
être attribuée à un service précis. **C'est une limite d'attribution majeure, elle doit être écrite.**

**Extraction.** Balayage ASCII et UTF-16LE, longueur minimale 6, puis **filtrage par forme de chemin** avant
toute autre chose (`[A-Za-z]:\`, `\Device\HarddiskVolume`, `file:///`, `\\?\`, UNC). Les motifs littéraux
`MonitorProcess` et `UDP Query` font l'objet d'une recherche séparée à confiance faible.

**Conservation.** Processus, PID, chaîne (tronquée), 64 octets de contexte, adresse de base de région,
type et protection de région, horodatage, statut d'existence du chemin sur disque.

**(5) Faux positifs.** Extrêmes si on applique la liste du §7 brute. Le filtrage « chemin **absent** du
disque ou situé dans Downloads/Temp/AppData » est ce qui rend le module exploitable. Le reste est archivé
en données brutes consultables, non scorées.

**Interdits.** `WriteProcessMemory`, `VirtualProtectEx`, `CreateRemoteThread`, toute injection.

---

## M05 · UsnJournalScanner (§8, §9, §10)

**(1) Détecte réellement.** L'historique des opérations sur le système de fichiers NTFS : création,
suppression, renommage, modification — **y compris pour des fichiers qui n'existent plus**. C'est la source
la plus riche du produit.

**(2) Limites.** Journal circulaire (L1), supprimable (L2), et la **reconstruction du chemin** est le vrai
problème : un enregistrement USN ne contient que le nom du fichier et la référence MFT du parent. Si le
dossier parent a été supprimé lui aussi, le chemin n'est pas reconstructible → chemin partiel, à afficher
comme tel plutôt que d'inventer.

**(3) API.** Handle sur `\\.\C:` (`FILE_READ_ATTRIBUTES | SYNCHRONIZE`, partage complet) →
- `FSCTL_QUERY_USN_JOURNAL` → `JournalID`, `FirstUsn`, `NextUsn`, `MaximumSize` (= fenêtre couverte, L1/L2) ;
- `FSCTL_READ_USN_JOURNAL` (`READ_USN_JOURNAL_DATA_V1`) par buffers de 1 Mo, parsing de `USN_RECORD_V2/V3` ;
- `FSCTL_ENUM_USN_DATA` **une seule fois** pour construire la table `FileReference → (nom, parent)`,
  ce qui permet de résoudre les chemins sans un `OpenFileById` par enregistrement (des milliers d'ouvertures).

**Aucun appel à `fsutil.exe`, `findstr` ou fichier `.txt` temporaire** — lecture native, comme exigé.

**Traitement des renommages.** NTFS émet une paire `USN_REASON_RENAME_OLD_NAME` puis
`USN_REASON_RENAME_NEW_NAME` avec le **même `FileReferenceNumber`** et des USN consécutifs. Le parseur
apparie ces paires et produit une observation unique :

```
Fichier    : C:\Users\...\AppData\Local\Temp\WindowsUpdate.exe
Événement  : RENOMMAGE
Ancien nom : cheat.exe
Nouveau    : WindowsUpdate.exe
Date       : 2026-08-27 21:14:38 UTC
Source     : USN Journal (C:) — USN 0x0000000ABC12
```

Vues dérivées demandées au §8, construites par filtrage : *tous les EXE · EXE créés · EXE supprimés ·
EXE renommés · anciens noms · nouveaux noms*.

**(4) Méthode plus fiable retenue.** Deux améliorations par rapport à la méthode manuelle :
- **Fenêtre de couverture explicite** : le rapport indique la période réellement couverte, pour qu'une
  absence de trace ne soit pas lue comme une innocence ;
- **Détection du journal réinitialisé** : `JournalID` récent ou `FirstUsn` postérieur à la dernière mise
  sous tension → observation anti-forensic (transmise à M17).

**(5) Faux positifs.** Considérables si l'on scorait les renommages bruts. Patterns légitimes **très**
fréquents à neutraliser :
- `*.tmp` → `*.exe` : mécanisme de mise à jour de Chrome, Discord, Steam, Edge ;
- `*.exe.download` / `*.crdownload` / `*.partial` → `*.exe` : téléchargements navigateur normaux ;
- créations massives sous `WinSxS`, `Windows\Installer`, `Package Cache` : Windows Update ;
- `node_modules`, dossiers de build, caches de compilateur.

Ces patterns sont dans `data/rules/whitelist.json`, donc modifiables sans recompiler.

---

## M06 · DeletedFilesScanner (§9)

Ce module ne fait **aucune** lecture de secteurs bruts et **aucune** récupération. Il agrège les traces de
suppression provenant des autres sources :

| Source | Ce qu'elle apporte | Fiabilité de la date |
|---|---|---|
| USN (`FILE_DELETE`) | Nom + chemin + date exacte | Élevée |
| Prefetch existant, exécutable absent | Preuve d'exécution passée | Moyenne (dernière exécution) |
| BAM / MuiCache / AppCompat pointant vers un chemin inexistant | Chemin complet | Moyenne |
| `.lnk` de `shell:recent` dont la cible n'existe plus | Chemin + timestamps du LNK | Moyenne |
| Corbeille `$I` / `$R` (métadonnées `$I` uniquement) | Nom d'origine, taille, date de suppression | Élevée |

Sortie normalisée : `Nom · Ancien chemin · Date estimée · Type · Source de l'information`.
Quand plusieurs sources concordent, la confiance monte — c'est le rôle du corrélateur, pas du module.

**Interdit.** Aucune restauration, aucune lecture d'espace non alloué, aucun *carving*.

---

## M07 · PrefetchScanner (§11)

**(1) Détecte réellement.** **Qu'un exécutable a été exécuté**, combien de fois, et quand (jusqu'à 8 dernières
exécutions sur Win10/11). Un `.pf` dont l'exécutable a disparu est le scénario « lancé puis supprimé » — la
signature forensic la plus utile du produit.

**(2) Limites.** Désactivable (L3). Le nom du `.pf` ne contient que le nom de l'exécutable + un hash de son
chemin — deux fichiers homonymes dans des dossiers différents donnent deux `.pf` distincts, mais le chemin
d'origine ne se lit que dans la liste de fichiers référencés à l'intérieur. Un vidage du dossier est possible
(et devient lui-même un indicateur).

**(3) API.** Lecture de `C:\Windows\Prefetch\*.pf` (admin requis). Depuis Windows 8, l'en-tête est `MAM\x04`
et le corps est compressé **Xpress Huffman** : décompression via
`RtlGetCompressionWorkSpaceSize` + `RtlDecompressBufferEx(COMPRESSION_FORMAT_XPRESS_HUFF)`.
Versions de format supportées : 23 (Win7), 26 (Win8.1), 30 v1 et v2 (Win10/11).
Extraction : nom, hash de chemin, `RunCount`, table des 8 `FILETIME`, volumes référencés (numéro de série +
date de création), liste des fichiers chargés.
Vérification de l'état : `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters\EnablePrefetcher`.

**(4) Méthode plus fiable retenue.** Croiser la **liste interne des fichiers référencés** (qui contient les
DLL chargées par le processus) avec les DLL suspectes de M14 : un `.pf` de `gmod.exe` référençant une DLL
d'un dossier `Temp` est bien plus parlant que le nom du `.pf` seul.

**(5) Faux positifs.** Installeurs et désinstalleurs laissent des `.pf` d'exécutables temporaires disparus —
c'est parfaitement normal. La règle « `.pf` sans exécutable » n'est donc pas suffisante seule ; le chemin
d'origine et la corrélation USN font la différence.

---

## M08 · RegistryExecutionScanner (§12)

Lecture **strictement en lecture seule** (`RegistryKeyPermissionCheck.ReadSubTree`,
`RegistryRights.ReadKey`, vue `Registry64`), aucune écriture, aucune création de clé.

| Artefact | Clé | Ce qu'il donne | Limites / faux positifs |
|---|---|---|---|
| **Compatibility Assistant** | `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store` | Chemins complets d'exécutables lancés (valeur binaire) | Alimenté seulement quand l'assistant se déclenche ; pas de date fiable dans la valeur |
| **MuiCache** | `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache` | Chemin + `FriendlyAppName` + éditeur déclaré | **Aucun horodatage par entrée** (seule la clé porte un `LastWriteTime` global) — ne jamais présenter une date par entrée |
| **FeatureUsage / AppLaunch** | `HKCU\...\Explorer\FeatureUsage\AppLaunch` | Compteur de lancements par AppID de barre des tâches | Seulement les applications épinglées/lancées depuis la barre des tâches |
| **FeatureUsage / AppSwitched** | `HKCU\...\Explorer\FeatureUsage\AppSwitched` | Compteur de bascules de focus | Idem |
| **UserAssist** *(ajout)* | `HKCU\...\Explorer\UserAssist\{GUID}\Count` | Nom ROT13, compteur, **date de dernière exécution** | Non demandé au §12 mais c'est le seul de la famille à donner une date fiable — ajouté |
| **ShimCache** *(ajout)* | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatCache` | Chemin + date de modification du fichier, ~1024 entrées | **N'est écrit sur disque qu'à l'arrêt** : les exécutions de la session en cours n'y sont pas encore |

**Méthode plus fiable retenue.** Ne jamais afficher une entrée MuiCache avec une pseudo-date. Préférer
UserAssist et BAM (M09) quand une date est nécessaire, et l'indiquer explicitement dans la colonne source.

**Faux positifs.** Ces clés enregistrent **toute** exécution, y compris celle de l'outil de contrôle
lui-même et de tout ce que le staff lance sur la machine. Le nom d'un exécutable dans MuiCache n'est
absolument pas un indicateur en soi.

---

## M09 · BamScanner (§12)

**(1) Détecte réellement.** *Background Activity Moderator* : pour chaque SID utilisateur, la liste des
exécutables lancés avec leur **date de dernière exécution**, sous forme de chemins NT
(`\Device\HarddiskVolume3\Users\...\x.exe`). C'est l'un des rares artefacts avec un horodatage fiable.

**(2) Limites.** Purge à environ 7 jours (L4). L'emplacement varie :
`...\Services\bam\State\UserSettings\{SID}` (Win10 1809+) ou `...\Services\bam\UserSettings\{SID}`
(builds antérieurs) — les deux sont testés. Absent de certaines éditions.

**(3) API.** `HKLM\SYSTEM\CurrentControlSet\Services\bam\...` en lecture seule. La valeur est binaire :
les **8 premiers octets** sont un `FILETIME` UTC. Résolution `\Device\HarddiskVolumeN` → lettre via
`QueryDosDevice`. Résolution SID → nom de compte via `LookupAccountSid`.

**(4) Méthode plus fiable retenue.** BAM donne un chemin **complet**, là où Prefetch ne donne qu'un nom.
Le corrélateur utilise donc BAM en priorité pour attribuer un chemin à une entrée Prefetch homonyme.

**(5) Faux positifs.** BAM liste absolument tout ce qui s'est exécuté, y compris les tâches système. Le
volume brut est important ; seul le croisement (chemin temporaire, fichier disparu, non signé) est
significatif.

---

## M10 · RecentFilesScanner (§13)

**(1) Détecte réellement.** Ce que l'utilisateur a **ouvert via le shell** : `%APPDATA%\Microsoft\Windows\Recent\*.lnk`.

**(2) Limites.** Ne couvre que les ouvertures passant par l'Explorateur ou une boîte de dialogue standard.
Facilement vidable — un dossier `Recent` vide sur un compte ancien est en soi une observation.

**(3) API.** Parsing natif du format **Shell Link Binary** (MS-SHLLINK) plutôt qu'une simple liste de noms :
en-tête, `LinkTargetIDList`, `LinkInfo` (chemin local, numéro de série du volume, type de lecteur),
`StringData`, et `ExtraData` — dont le `TrackerDataBlock` qui contient le **nom NetBIOS de la machine
d'origine et l'adresse MAC** encodés dans le *Droid* GUID.
Timestamps du LNK = ceux de la **cible au moment de la création du raccourci**, distincts de ceux du `.lnk`.

**(4) Méthode plus fiable retenue.** Le parsing binaire complet apporte trois informations que
`dir shell:recent` ne donne pas : le **chemin complet** de la cible (même supprimée), la **taille** du
fichier à l'époque, et le **numéro de série du volume** — ce qui permet de rattacher un fichier ouvert à
une **clé USB** identifiée par M12. C'est le lien le plus utile entre les deux modules.
Extension prévue : les *Jump Lists* (`AutomaticDestinations\*.automaticDestinations-ms`, conteneur OLE CF)
pour les applications ne passant pas par `Recent`.

**(5) Faux positifs.** Un `.zip` ou un `.exe` dans `Recent` signifie « ouvert », pas « exécuté », et encore
moins « cheat ». Extensions filtrées : `.exe .dll .bat .cmd .ps1 .jar .lua .zip .rar .7z` (configurable).

---

## M11 · ArchiveHistoryScanner (§14)

**(3) API.** Lecture seule de :
- `HKCU\SOFTWARE\WinRAR\ArcHistory` — chemins des archives récemment ouvertes ;
- `HKCU\SOFTWARE\WinRAR\DialogEditHistory\ExtrPath` — **dossiers de destination d'extraction** ;
- *(ajout)* `HKCU\Software\7-Zip\Compression\ArcHistory` (valeur `REG_BINARY`, chaînes UTF-16 concaténées)
  et `HKCU\Software\7-Zip\FM\FolderHistory` ;
- *(ajout)* `HKCU\Software\WinRAR\General\LastFolder`.

**(4) Méthode plus fiable retenue.** L'historique **d'extraction** est plus intéressant que celui
d'ouverture : il indique *où* le contenu a été déposé, ce qui oriente le `FileSystemScanner` (M13) vers les
bons dossiers et permet de recouper avec l'USN à l'horodatage correspondant.

**(2) Limites.** Ces clés n'ont pas d'horodatage par entrée (seul l'ordre est significatif : `0` = le plus
récent), et le `LastWriteTime` de la clé ne date que la dernière opération. Ne couvre pas l'Explorateur
Windows ni 7-Zip portable en mode sans registre.

**(5) Faux positifs.** Élevés — ouvrir une archive est banal. N'a de valeur qu'associé au nom de l'archive
et à ce qui a été extrait.

**Interdit.** Aucune extraction, aucune ouverture du contenu de l'archive.

---

## M12 · UsbScanner (§15)

**(1) Détecte réellement.** Les périphériques de stockage amovibles **présents** et **historiquement
connectés** — donc un vecteur possible de transfert de fichiers.

**(3) API et sources.**

| Source | Apport |
|---|---|
| `SetupDiGetClassDevs` / `SetupDiEnumDeviceInfo` (+ `CM_Get_Device_ID`) | Périphériques **présents** : PNP Device ID, description, fabricant, statut |
| `HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR` | Historique : fabricant, modèle, révision, **numéro de série** (`&0` en 2ᵉ position = série non unique fournie par le périphérique) |
| Sous-clé `Properties\{83da6326-97a6-4088-9453-a1923f573b29}\` valeurs `0064` / `0066` / `0067` | **Première installation**, **dernière connexion**, **dernier retrait** |
| `HKLM\SYSTEM\MountedDevices` | Lettre de lecteur ↔ signature de volume |
| `HKCU\...\Explorer\MountPoints2` | Quel **utilisateur** a monté le volume |
| `C:\Windows\INF\setupapi.dev.log` | Horodatage de la **première** installation du pilote |
| Event Log `Microsoft-Windows-Partition/Diagnostic` (id 1006) | Connexion/déconnexion avec modèle et série |

**(4) Méthode plus fiable retenue.** L'approche manuelle (regarder `USBSTOR`) ne dit pas *quand*.
La combinaison `Properties\{83da6326...}\0064/0066/0067` + `Partition/Diagnostic` donne une **chronologie**,
qui peut ensuite être recoupée avec les créations de fichiers USN à la même minute — c'est ce croisement qui
répond réellement à « des fichiers ont-ils été transférés depuis une clé ? ».

**(2) Limites.** `Microsoft-Windows-DriverFrameworks-UserMode/Operational` est désactivé par défaut sur
Windows 10/11 (souvent cité, rarement disponible). Le numéro de série n'est pas toujours unique.

**(5) Faux positifs.** Souris, claviers, casques, manettes, téléphones en charge : filtrage sur les classes
`DiskDrive` / `Volume` / `WPD`. **La présence d'une clé USB n'est jamais un indicateur en soi** — seule
compte la corrélation temporelle avec d'autres artefacts.

---

## M13 · EventLogScanner

Non listé explicitement au §4, mais c'est l'une des sources les plus solides.

**(3) API.** `System.Diagnostics.Eventing.Reader` (`EvtQuery`), requêtes XPath ciblées avec fenêtre
temporelle — jamais de lecture intégrale des journaux.

| Canal / ID | Signification | Valeur |
|---|---|---|
| `Microsoft-Windows-CodeIntegrity/Operational` **3033 / 3001** | Chargement d'image bloqué : signature invalide | **Très forte** — signature typique d'une tentative de driver de cheat |
| `System` **7045** | Nouveau service installé | **Très forte** — installation de driver |
| `System` **219** (Kernel-PnP) | Chargement d'un pilote | Forte |
| `Microsoft-Windows-Windows Defender/Operational` **1116 / 1117** | Détection / action | Forte, avec le nom de la menace |
| `Microsoft-Windows-Windows Defender/Operational` **5001 / 5007** | Protection désactivée / configuration modifiée | Forte |
| `Security` **1102**, `System` **104** | **Journal effacé** | Forte (anti-forensic → M17) |
| `Security` **4688** | Création de processus + ligne de commande | Excellente **si activée** — désactivée par défaut (L5) |
| `Application` — `Application Error` | Plantage d'`explorer.exe` | Contexte pour M03 |
| `Microsoft-Windows-TaskScheduler/Operational` | Tâche planifiée créée/exécutée | Moyenne |

**(4) Méthode plus fiable.** Là où le cahier des charges cherche des cheats par leurs fichiers, les
événements CodeIntegrity et 7045 capturent une classe de cheats (kernel) que **aucune** autre méthode du
document ne détecte. C'est l'ajout à plus forte valeur.

**(5) Faux positifs.** 7045 est déclenché par tout installeur légitime (pilotes GPU, VPN, anti-triche de
jeux, outils de monitoring). CodeIntegrity 3033 se déclenche aussi pour des pilotes anciens mal signés.

---

## M14 · FileSystemScanner + SuspiciousFileAnalyzer (§18, §19, §20)

**(3) Méthode.** Parcours **ciblé** — jamais le disque entier (§26) : racines configurables
(`Downloads`, `Desktop`, `Documents`, `AppData`, `Temp`, bibliothèques Steam et `GarrysMod`), profondeur
maximale, filtrage par extension, exclusions. `FileSystemEnumerable` avec
`IgnoreInaccessible = true` et `AttributesToSkip = ReparsePoint` (pas de boucle sur les jonctions).
Découverte des bibliothèques Steam via `steamapps\libraryfolders.vdf` (lu comme **données texte**).

**Par fichier retenu.** Nom, chemin, extension, taille, dates création/modification/accès (UTC),
attributs, SHA-256 (lecture seule, plafonnée), signature Authenticode + sujet du certificat + chaîne de
confiance, **flux `Zone.Identifier`** (voir M15), et **cohérence extension / en-tête réel**
(un `.jpg` commençant par `MZ`, un `.dll` sans en-tête PE).

**DLL suspectes (§19).** Critères observés : non signée, hors des chemins système, créée récemment,
présente dans `Temp`/`AppData`, absente du disque mais référencée par un Prefetch, renommée d'après l'USN,
chargée par un processus lié à GMod.

**Indicateurs GMod (§20).** `.lua` hors des dossiers `garrysmod` attendus, `.gma` hors de `addons`/`cache`,
DLL dans `garrysmod\lua\bin\` (emplacement des modules binaires — légitime pour certains addons,
mais c'est le point d'entrée classique), fichiers dans `GarrysMod\bin\` non signés par Valve.

**(4) Méthode plus fiable.** Les noms du §18 (`cheat.exe`, `injector.exe`, `loader.exe`, `bypass.exe`,
`mapper.exe`, `spoof.exe`, `exploit.exe`) sont dans `data/rules/cheat-indicators.json` et sont pondérés
**très bas s'ils sont seuls**. Un cheat réellement utilisé porte rarement son nom. Les signaux robustes sont
plutôt : *fichier PE non signé récemment créé dans `Temp`* + *trace Prefetch* + *disparu depuis*.

**(5) Faux positifs.** Énormes sur le nom seul : dossiers de développement, projets d'apprentissage,
outils de sécurité légitimes, dépôts clonés, jeux moddés. **Ne jamais sanctionner sur le nom** (exigence
explicite du §18, respectée par la pondération).

---

## M15 · ZoneIdentifierScanner *(ajout justifié)*

**Pourquoi.** Le §16 demande de détecter les téléchargements depuis Discord. La méthode la plus fiable ne
passe **pas** par les données de Discord : lorsqu'un fichier est téléchargé, Windows attache un flux de
données alternatif `:Zone.Identifier` contenant :

```
[ZoneTransfer]
ZoneId=3
ReferrerUrl=https://discord.com/channels/...
HostUrl=https://cdn.discordapp.com/attachments/.../loader.dll
```

**(3) API.** `FindFirstStreamW` / `FindNextStreamW` pour énumérer les flux, puis lecture de
`chemin:Zone.Identifier:$DATA` en lecture seule.

**(4) Pourquoi c'est meilleur.** On obtient **l'URL source exacte et le fichier concerné**, sans ouvrir la
moindre donnée applicative de Discord, sans cookie, sans token. Cela couvre aussi les téléchargements
depuis n'importe quel autre hébergeur (MEGA, Google Drive, GitHub, sites de cheats), ce que la recherche
`cdn.discordapp.com` seule ne fait pas. **Meilleure couverture, intrusion plus faible.**

**(2) Limites.** Le flux est supprimé si le fichier est déplacé vers un volume non-NTFS, s'il est « débloqué »
volontairement, ou si l'archive est extraite par un outil qui ne le propage pas. Ne survit pas au fichier.

**(5) Faux positifs.** `ZoneId=3` signifie « venu d'Internet » — vrai pour la quasi-totalité des logiciels
installés légitimement. Seul le couple (hôte source, type de fichier, emplacement) est significatif.

---

## M16 · DiscordScanner (§16)

**Périmètre strictement borné. Ce module NE lit PAS :** les messages, `Local Storage` / `leveldb`
(qui contient le token), `Cookies`, `Login Data`, les bases de données de session, ni aucun secret.
Ces exclusions sont codées en dur sous forme de liste de chemins refusés, et vérifiées par un test unitaire.

**Ce qu'il lit.**
1. **Présence et version** : `%APPDATA%\discord\` (ou `discordptb`/`discordcanary`), fichier `settings.json`
   pour la seule version, `%LOCALAPPDATA%\Discord\app-*`.
2. **Clés d'URL du cache HTTP** : le cache Chromium (`%APPDATA%\discord\Cache\Cache_Data\`) stocke les URLs
   comme clés d'entrées. Le module extrait **uniquement les chaînes d'URL** correspondant à
   `cdn.discordapp.com/attachments/…` ou `media.discordapp.net/…` dont l'extension est dans la liste
   d'intérêt (`.exe .dll .jar .bat .cmd .zip .rar .7z .lua .gma`). **Aucun corps de réponse n'est lu ni
   conservé** — pas de contenu, seulement des noms de ressources.
3. **Corrélation** avec M15 (Zone.Identifier) et l'USN : un fichier téléchargé depuis le CDN Discord et
   présent dans `Downloads` à l'horodatage correspondant.

Sortie :

```
Source            : Discord (cache HTTP)
Indicateur        : cdn.discordapp.com/attachments/.../example.dll
Fichier associé   : C:\Users\...\Downloads\example.dll  (confirmé par Zone.Identifier)
Date              : 2026-08-27 20:58 UTC
Confiance         : Moyenne
```

**(2) Limites.** Le cache est purgé régulièrement et par l'utilisateur. Le nom du fichier dans l'URL n'est
pas toujours le nom sur disque. Discord n'est qu'un vecteur parmi d'autres.

**(5) Faux positifs.** Le partage de fichiers sur Discord est massivement légitime (mods, screenshots,
outils, addons GMod). L'observation seule ne vaut rien ; c'est la nature du fichier téléchargé qui compte.

---

## M17 · CredentialTraceScanner — OINK (§17)

**(1) Détecte réellement.** L'existence d'une entrée nommée dans le Gestionnaire d'identifiants Windows.
Rien de plus. Aucune information sur un usage.

**(3) API.** `CredEnumerateW(NULL, CRED_ENUMERATE_ALL_CREDENTIALS, &count, &creds)` puis, pour chaque
`CREDENTIALW`, lecture de **`TargetName`, `UserName`, `Type`, `LastWritten`, `Comment` uniquement**.

**Garantie d'implémentation.** La structure est marshalée par un `struct` maison dans lequel les champs
`CredentialBlobSize` et `CredentialBlob` sont déclarés mais **jamais déréférencés** ; un test unitaire vérifie
qu'aucun chemin de code n'y accède, et le rapport ne contient aucun champ pouvant les recevoir.
`CredFree` est appelé systématiquement. Aucun appel à `CredRead` avec déchiffrement, aucun accès à
`%APPDATA%\Microsoft\Credentials`, aucune DPAPI, aucun contournement.

**Sortie.**

```
OINK détecté : OUI
Entrée       : LegacyGeneric:target=OINK_ACCOUNT
Utilisateur  : oink_user
Type         : Generic
Modifié le   : 2026-08-12 18:41 UTC
Secret       : NON LU — le mot de passe n'est jamais accédé par ce logiciel
```

**(2) Limites.** `CredEnumerate` ne voit que le coffre de **l'utilisateur exécutant le processus**. Si le
scanner est lancé sous un autre compte que celui du joueur, le résultat sera vide alors que l'entrée
existe. Cette limite doit être affichée avec le résultat, sinon un « NON » est trompeur.

**(5) Faux positifs.** Un nom contenant « OINK » peut appartenir à autre chose. Une entrée peut avoir été
créée il y a longtemps sans usage récent.

---

## M18 · AntiForensicScanner *(ajout justifié)*

Regroupe les observations de « traces effacées », qui sont dispersées dans les autres modules mais dont
la lecture combinée est le signal le plus fort qu'un outil de contrôle puisse produire :

| Observation | Source |
|---|---|
| Journal USN supprimé/recréé récemment | M05 (`JournalID`, `FirstUsn`) |
| Dossier Prefetch vide ou `EnablePrefetcher = 0` sur une machine ancienne | M07 |
| Journal d'événements effacé (`Security` 1102 / `System` 104) | M13 |
| `Recent` vide sur un compte utilisé depuis des mois | M10 |
| BAM vide alors que la machine tourne depuis longtemps | M09 |
| Corbeille vidée juste avant le contrôle | M06 |
| Exécution récente d'un outil de nettoyage connu (BleachBit, CCleaner, PrivaZer…) | M07/M09 |
| Horloge système décalée de façon anormale | M01 |

**Faux positifs.** Machine récemment réinstallée, disque SSD avec Prefetch désactivé par un « optimiseur »,
usage normal d'un outil de nettoyage pour la vie privée, entreprise appliquant une politique de purge.
**Une machine fraîchement réinstallée coche presque toutes ces cases** — le module doit donc toujours
afficher la date d'installation de Windows à côté de ses conclusions. C'est indispensable.

---

## Récapitulatif des ajouts par rapport au §4

| Module ajouté | Justification |
|---|---|
| **M13 EventLogScanner** | Seule source couvrant les cheats à composant kernel (CodeIntegrity, service 7045) |
| **M15 ZoneIdentifierScanner** | Détecte l'origine réelle des téléchargements avec une intrusion moindre que l'analyse de Discord, et couvre tous les hébergeurs |
| **M18 AntiForensicScanner** | Le nettoyage de traces est ce qu'un staff cherche en priorité ; l'information existait mais restait dispersée |
| **M01 SystemInfoScanner** | Test-signing et Secure Boot désactivé sont des prérequis fréquents au chargement d'un driver de cheat |

Ces quatre ajouts ne modifient pas le périmètre : ils restent passifs, en lecture seule, et n'exigent aucun
accès supplémentaire.

---

**Étape suivante** : `docs/04-detection-scoring.md` — moteur de corrélation, règles, scoring contextuel,
protection contre les faux positifs.
