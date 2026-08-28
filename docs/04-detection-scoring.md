# Étape 2 (suite) — Moteur de détection, scoring et maîtrise des faux positifs

Couvre les §21 (scoring), §22 (faux positifs) et l'exigence transversale « détection ≠ preuve ».

---

## 1. Chaîne de traitement

```
Observation[]  (tous modules confondus)
      │
      ▼  ① Normalisation      chemins, volumes, casse, variables d'environnement → FileKey
      │
      ▼  ② Corrélation        regroupement par entité (fichier, processus, périphérique)
      │
      ▼  ③ Suppression        KnownSoftware / Whitelist → l'entité disparaît du flux
      │
      ▼  ④ Règles atomiques   1 fait  → 1 contribution
      │
      ▼  ⑤ Règles composites  N faits → 1 détection consolidée (remplace les atomiques)
      │
      ▼  ⑥ Atténuation        signature valide, éditeur connu, chemin attendu → sévérité et confiance ↓
      │
      ▼  ⑦ Agrégation         score global saturant, plafonds par catégorie
      │
      ▼  Detection[] + ScoreBreakdown + score global
```

Les étapes ③ et ⑥ existent **avant** et **après** la production des détections : on filtre le bruit en amont
pour ne pas le scorer, et on atténue en aval ce qui a survécu mais s'explique légitimement.

---

## 2. Corrélation : ce qui fait la valeur du produit

Le corrélateur regroupe les observations par **entité** :

- **Entité fichier** — jointure par `FileKey` (voir `docs/02` §3.2). Un même fichier peut être vu par
  l'USN (référence MFT), le Prefetch (nom + hash de chemin), BAM (chemin NT), la mémoire (chemin littéral)
  et le système de fichiers (chemin + SHA-256). La fusion est ce qui permet la phrase
  *« exécuté le 27/08 à 21 h 12, renommé à 21 h 14, supprimé à 21 h 15, jamais revu »*.
- **Entité processus** — PID + chemin image + heure de démarrage.
- **Entité périphérique** — PNP Device ID + numéro de série.
- **Entité temporelle** — regroupement des faits dans une fenêtre glissante (défaut 5 min) : c'est ce qui
  révèle les séquences (branchement USB → création de fichiers → exécution → suppression).

Chaque entité porte la liste de ses **sources indépendantes**. Le nombre de sources indépendantes est le
principal multiplicateur de confiance : un fait vu par une seule source reste faible, quel que soit son type.

---

## 3. Règles

### 3.1 Règles atomiques (déclaratives, en JSON)

```jsonc
{
  "id": "USN.EXE_DELETED_IN_DOWNLOADS",
  "when": {
    "kind": "FileDeleted",
    "module": "usn",
    "path": { "underAnyOf": ["%USERPROFILE%\\Downloads", "%LOCALAPPDATA%\\Temp"] },
    "extension": [".exe", ".dll"]
  },
  "severity": "Medium",
  "points": 15,
  "confidence": "Medium",
  "name": "Exécutable supprimé dans un dossier de téléchargement",
  "explanation": "Un fichier exécutable a été supprimé récemment dans {path}. La suppression seule est banale ; elle n'a de valeur qu'associée à d'autres traces.",
  "falsePositiveNote": "Nettoyage manuel du dossier Téléchargements, désinstalleur, mise à jour d'application, purge automatique de Temp par Windows."
}
```

`falsePositiveNote` est **obligatoire dans le schéma** : le chargeur de règles refuse une règle sans ce champ.
Une règle qui ne sait pas dire comment elle peut se tromper n'entre pas dans le produit.

### 3.2 Règles composites (en C#, testables)

C'est là que se trouve la véritable détection. Exemple correspondant à l'illustration du §21 :

```csharp
// COMPOSITE.EXECUTED_THEN_ERASED
// Un exécutable a été lancé, puis a disparu, et plusieurs sources indépendantes le confirment.
if (entity.Has(ObservationKind.PrefetchEntry)              // trace d'exécution
 && entity.Has(ObservationKind.FileDeleted)                // suppression constatée
 && !entity.ExistsOnDisk
 && entity.IndependentSourceCount >= 2)
{
    var score = new ScoreBreakdown();
    score.Add("PREFETCH_PRESENT",   "Trace d'exécution trouvée dans le Prefetch",        +15);
    score.Add("USN_DELETED",        "Suppression confirmée par le journal USN",          +15);
    if (entity.WasRenamed)
        score.Add("RENAMED_BEFORE_DELETE",
                  $"Renommé « {entity.OldName} » → « {entity.NewName} » avant suppression", +20);
    if (entity.IsUnderUserDownloadOrTemp)
        score.Add("USER_TEMP_PATH", "Situé dans un dossier de téléchargement ou temporaire", +10);
    // → total 60/100 pour cette détection, exactement comme l'exemple du §21
}
```

Une règle composite **remplace** les détections atomiques des observations qu'elle consomme
(`SupersededObservationIds`), ce qui évite le double comptage et produit un rapport lisible : une ligne
« exécuté puis effacé », plutôt que quatre lignes dispersées dans quatre sections.

Familles de composites prévues :

| Composite | Faits combinés |
|---|---|
| `EXECUTED_THEN_ERASED` | Prefetch/BAM + suppression USN + absence disque |
| `DOWNLOADED_THEN_EXECUTED_THEN_ERASED` | Zone.Identifier ou Discord + Prefetch + suppression |
| `RENAMED_TO_SYSTEM_LOOKALIKE` | Renommage USN vers un nom de binaire système hors de `System32` |
| `USB_TRANSFER_WINDOW` | Connexion USB + créations de fichiers dans les 5 min + exécution |
| `KERNEL_DRIVER_ATTEMPT` | CodeIntegrity 3033 ou service 7045 + binaire non signé |
| `ANTI_FORENSIC_PATTERN` | ≥ 2 observations M18 sur une machine non récemment réinstallée |
| `MEMORY_PATH_TO_MISSING_FILE` | Chemin en mémoire + fichier absent + confirmation USN de suppression |

---

## 4. Scoring

### 4.1 Deux niveaux, deux formules — et c'est volontaire

**Niveau 1 — à l'intérieur d'une détection : addition** (exactement le modèle du §21).

```
+15  EXE récemment supprimé
+15  EXE trouvé dans le Prefetch
+20  Ancien nom potentiellement suspect
+10  Fichier provenant du dossier Downloads
──────────────────────────────────────────
      60 / 100
```

Barème de base conforme au §21 : `Faible +5 · Moyen +15 · Élevé +30 · Critique +50`.
Résultat borné à `[0, 100]`.

**Niveau 2 — score global : agrégation saturante, jamais une somme.**

Une simple addition donnerait « 340/100 » sur une machine ordinaire. La formule retenue est la
**probabilité complémentaire** :

```
        n
S = 100 × ( 1 − Π ( 1 − (pᵢ × cᵢ) / 100 ) )
       i=1
```

où `pᵢ` est le score de la détection *i* et `cᵢ` son coefficient de confiance
(`Faible = 0,4 · Moyenne = 0,7 · Élevée = 1,0`).

Propriétés obtenues, toutes nécessaires ici :
- le score **ne dépasse jamais 100** sans clamp artificiel ;
- deux indicateurs moyens pèsent **plus** qu'un seul, mais **moins** que leur somme ;
- une confiance faible réduit réellement la contribution, au lieu d'être décorative ;
- un seul indicateur critique (50, confiance élevée) donne 50 — il ne suffit jamais à saturer le score,
  ce qui est cohérent avec « aucun élément n'est une preuve à lui seul ».

**Correctifs anti-spam** (indispensables : un scan produit des centaines de faits) :
- **Rendements décroissants par règle** : la *k*-ième détection d'une même règle est pondérée `1/k`.
  Cent `.exe` supprimés dans `Temp` ne font pas un score de 100.
- **Plafond par catégorie** : aucune catégorie (USN, Prefetch, Registre…) ne peut à elle seule dépasser
  60 points de contribution. Un score élevé exige donc des **sources de natures différentes**.
- **Plancher de corrélation** : une détection dont toutes les observations proviennent d'un module unique
  est plafonnée à la sévérité `Medium`.

### 4.2 Bandes d'interprétation

| Score | Libellé affiché | Formulation imposée |
|---|---|---|
| 0 – 19 | Aucun indicateur notable | « Aucun élément marquant sur la fenêtre couverte » |
| 20 – 39 | Indicateurs faibles | « Éléments à contextualiser » |
| 40 – 59 | Indicateurs modérés | « Vérification manuelle recommandée » |
| 60 – 79 | Indicateurs élevés | « Vérification manuelle nécessaire » |
| 80 – 100 | Indicateurs très élevés | « Faisceau d'indicateurs concordants — vérification manuelle prioritaire » |

Aucune bande ne dit « triche », « coupable » ou « confirmé ». Le score qualifie **le dossier d'indicateurs**,
jamais la personne. Un bandeau permanent du rapport le rappelle, et la fenêtre temporelle réellement couverte
est affichée à côté du score (un 0/100 sur un journal USN de 2 jours ne veut pas dire la même chose que sur
30 jours).

---

## 5. Base de connaissance et maîtrise des faux positifs (§22)

Quatre fichiers JSON dans `data/rules/`, chargés au démarrage, surchargeables depuis
`%ProgramData%\GModForensicScanner\rules\` — **modifiables sans recompiler**, comme exigé.

### 5.1 `known-software.json` — suppression en amont

```jsonc
{
  "version": "2026.08.1",
  "entries": [
    {
      "name": "Discord",
      "publishers": ["Discord Inc."],
      "paths": ["%LOCALAPPDATA%\\Discord\\**", "%APPDATA%\\discord\\**"],
      "benignPatterns": [
        { "kind": "FileRenamed", "from": "*.tmp",     "to": "*.exe" },
        { "kind": "FileCreated", "path": "**\\app-*\\**" }
      ]
    },
    {
      "name": "Google Chrome",
      "publishers": ["Google LLC"],
      "benignPatterns": [
        { "kind": "FileRenamed", "from": "*.crdownload", "to": "*" },
        { "kind": "FileRenamed", "from": "*.tmp",        "to": "*.exe" }
      ]
    }
    // Steam, Firefox, NVIDIA, AMD, Intel, Microsoft, Visual Studio, Windows Update, Defender, ...
  ]
}
```

Une correspondance **éditeur signé + chemin attendu + pattern connu** supprime l'observation avant tout
scoring. C'est ce qui neutralise le piège majeur identifié en M05 : les mises à jour d'applications
renomment massivement des `.tmp` en `.exe`, ce qui ferait autrement exploser le score de toute machine.

### 5.2 `whitelist.json` — exclusions par motif

Chemins (`C:\Windows\WinSxS\**`, `**\node_modules\**`, `**\Windows\Installer\**`), hashes SHA-256 connus,
noms de processus système. Chaque entrée porte un `reason` obligatoire, pour audit.

### 5.3 `blacklist.json` — correspondances fortes

SHA-256 de binaires de cheat identifiés, noms de services et de drivers connus, chaînes de certificats
révoquées. Une correspondance par **hash** est le seul cas produisant directement une sévérité `Critical`
avec confiance `Élevée` — parce que c'est le seul cas où l'identification est exacte. Un nom, jamais.

### 5.4 `cheat-indicators.json` — motifs pondérés

Les noms du §18 (`cheat`, `injector`, `loader`, `bypass`, `mapper`, `spoof`, `exploit`), les motifs mémoire
du §7, les chemins d'intérêt GMod. **Chaque entrée porte son propre poids, volontairement bas en isolation** :
un fichier nommé `loader.exe` seul vaut `+5`, pas `+30`. L'exigence « ne jamais bannir sur la base du nom
seul » est ainsi appliquée par les données, pas par la bonne volonté du code.

### 5.5 Atténuateurs

| Condition | Effet |
|---|---|
| Signature Authenticode valide + éditeur dans `known-software.json` | Sévérité −2 niveaux, confiance → Faible |
| Signature valide, éditeur inconnu | Sévérité −1 niveau |
| Fichier présent depuis plus de 6 mois et jamais renommé | Sévérité −1 niveau |
| Machine installée il y a moins de 7 jours | Neutralise les règles `ANTI_FORENSIC_*` |
| Chemin dans une bibliothèque Steam et fichier signé par Valve | Suppression |

---

## 6. Non-régression sur les faux positifs

Un scoring anti-faux-positifs ne se démontre pas, il se **teste**. Le projet embarque une suite dédiée :

- **Jeux d'observations figés** représentant des machines saines : poste de développement (Visual Studio,
  Git, node), poste joueur (Steam + GMod moddé + addons), poste fraîchement réinstallé, poste avec pilotes
  NVIDIA et périphériques USB variés.
- **Assertion** : score global < 20 sur chacun de ces jeux.
- **Jeux d'observations positifs** : scénarios reconstruits (téléchargé → exécuté → renommé → supprimé,
  driver bloqué par CodeIntegrity), avec assertion de bande minimale.
- Tout ajout de règle doit maintenir ces deux garanties, sous peine d'échec de la CI.

C'est la traduction concrète du §22 : la protection contre les faux positifs est une **condition de build**,
pas une intention.

---

**Étape suivante** : `docs/05-ui-et-rapport.md` — interface WPF, rapports HTML/JSON/TXT, journalisation.
