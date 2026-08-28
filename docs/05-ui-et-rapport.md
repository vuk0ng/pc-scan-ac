# Étape 2 (suite) — Interface, rapports, journalisation et gestion d'erreurs

Couvre les §3 (interface), §23 (rapport), §24 (logs), §25 (erreurs), §26 (performance).

---

## 1. Interface (WPF · MVVM · thème sombre)

Quatre écrans, une seule fenêtre, navigation par vues.

### 1.1 Accueil / configuration

- Bandeau d'état des privilèges : `Administrateur : OUI · SeDebugPrivilege : OUI · SeSecurityPrivilege : NON`,
  suivi de la liste explicite des modules qui seront **ignorés** et de la raison (exigence §2).
- Champs de traçabilité : nom du membre du staff, identifiant du sujet, référence du contrôle.
- Case de consentement (obligatoire pour démarrer), horodatée dans le rapport.
- Sélection des modules et des répertoires à analyser (§20 : configuration des répertoires).
- Profils : `Rapide` (registre + Prefetch + processus, < 30 s) · `Standard` (défaut) ·
  `Approfondi` (+ mémoire, + USN complet).

### 1.2 Écran de scan

```
  SCANNER FORENSIC GMod

  [ Analyse du système ]

  ██████████████████░░░░  78 %

  Étape actuelle :
  Analyse du journal USN...

  Éléments analysés  : 18 492
  Indicateurs détectés : 7
  Temps écoulé        : 00:01:34

  ✓ Informations système        ✓ Processus            ✓ Prefetch
  ✓ Registre (exécutions)       ⟳ Journal USN          ○ Mémoire
  ⚠ Journal Security            ✕ USN sur D:           ○ Discord

  [ Annuler ]
```

Chaque module affiche son état en direct : `○` en attente · `⟳` en cours · `✓` réussi · `⚠` partiel ·
`✕` impossible. Un clic sur un module ouvre son journal et le motif exact d'un `⚠`/`✕`.

Le compteur « indicateurs détectés » est **provisoire** (issu des règles atomiques) et signalé comme tel ;
le chiffre définitif ne peut sortir qu'après corrélation, car les composites fusionnent des détections.

**Annulation** : bouton toujours actif ; le jeton d'annulation est propagé à tous les modules ; les résultats
partiels sont conservés et exportables, avec la mention « scan interrompu » en tête de rapport.

### 1.3 Écran de résultats

```
  SCAN TERMINÉ                       Fenêtre couverte : 14/08 → 28/08 (14 j)

  Score de suspicion : 72 / 100      Indicateurs élevés
                                     Vérification manuelle nécessaire

  Critiques : 2    Élevés : 3    Moyens : 2    Faibles : 5

  [ Voir les résultats ]  [ Exporter le rapport ]
```

La liste des détections est virtualisée (`VirtualizingStackPanel`, jusqu'à ~100 000 lignes),
filtrable par sévérité, module, catégorie et plage de dates, avec recherche plein texte.
Le panneau de détail d'une détection affiche : explication en français, **détail ligne à ligne du score**,
note de faux positifs, sources, chronologie de l'entité, et la **preuve brute** (valeur de registre, hex du
record USN, champs Prefetch, chaîne mémoire avec son contexte) accompagnée du `VerificationHint` indiquant
où re-regarder à la main.

Un onglet « Données brutes » donne accès à **tout** ce qui a été collecté, y compris ce qui n'a produit
aucune détection — c'est l'exigence de transparence du §1.

### 1.4 Fil de threads

Le scan s'exécute sur `Task.Run`. La progression remonte via `IProgress<T>` (marshalage automatique sur le
dispatcher). Aucune opération bloquante sur le thread UI ; l'interface reste réactive et annulable pendant
toute la durée du scan.

---

## 2. Rapports (§23)

### 2.1 Sections

`INFORMATIONS SYSTÈME · PROCESSUS · EXÉCUTABLES · DLL · JOURNAL USN · FICHIERS SUPPRIMÉS ·
FICHIERS RENOMMÉS · PREFETCH · REGISTRE · FICHIERS RÉCENTS · USB · DISCORD · ARCHIVES · OINK ·
INDICATEURS GMod · ANTI-FORENSIC · SCORE GLOBAL`

Pour chaque détection : `Nom · Type · Chemin · Date · Source · Indicateur · Niveau · Confiance · Explication`,
plus la note de faux positifs et le détail du score.

### 2.2 HTML — autonome et lisible par un non-technicien

- **Un seul fichier**, CSS et JS **intégrés**. Aucune ressource externe, aucun CDN : le rapport doit
  s'ouvrir sur une machine hors ligne et rester lisible dans dix ans.
- En-tête : sujet, staff, date UTC et locale, version du scanner, version des règles,
  **fenêtre temporelle couverte**, **empreinte SHA-256 du rapport JSON associé** (intégrité).
- Bandeau permanent, non masquable :
  > *Ce document recense des indicateurs. Aucun élément ci-dessous ne constitue à lui seul une preuve
  > d'utilisation de cheat. Toute conclusion doit reposer sur une vérification manuelle.*
- Sommaire cliquable, sections repliables, tri et filtre par sévérité, code couleur accessible
  (contrastes ≥ 4.5:1, la couleur n'est jamais le seul porteur d'information : un libellé texte l'accompagne).
- Chronologie unifiée de tous les événements horodatés, toutes sources confondues — c'est la vue la plus
  utile pour un staff, et elle n'existe dans aucun des outils manuels.
- **Sécurité (L11)** : chaque valeur est échappée HTML sans exception (les noms de fichiers sont contrôlés
  par la personne analysée). En-tête `Content-Security-Policy` restrictive via balise `<meta>`,
  aucun `innerHTML`, aucun `eval`. Un test unitaire vérifie qu'une détection portant un nom
  `<script>alert(1)</script>.exe` produit un rapport inerte.

### 2.3 JSON — exploitable par des outils

Schéma versionné (`schemaVersion`), stable, contenant :
métadonnées du scan · capacités et privilèges obtenus · résultat et diagnostics de chaque module ·
**toutes** les observations brutes · toutes les détections avec leur `ScoreBreakdown` ·
le journal d'exécution · le **journal d'accès** (chaque ressource effectivement lue).
Sérialisation `System.Text.Json` source-generated, dates en ISO 8601 UTC.

### 2.4 TXT

Résumé condensé destiné au collage dans un ticket ou un salon staff : en-tête, score, répartition, dix
principales détections avec chemin et date, mention de renvoi vers le rapport HTML.

### 2.5 Écriture

Un composant unique, `ReportOutputWriter`, est le **seul** autorisé à écrire sur disque, et uniquement sous
le dossier de sortie choisi par le staff (`Documents\GModForensicScanner\Reports\<horodatage>\` par défaut).
Aucun binaire suspect n'est copié : uniquement hashes et métadonnées.

---

## 3. Journalisation (§24)

```
[14:32:01] [scan]      Scan démarré — profil Standard — opérateur : <staff> — sujet : <id>
[14:32:01] [privileges] Élévation OUI · SeDebugPrivilege OUI · SeSecurityPrivilege NON
[14:32:01] [privileges] Module « Journal Security » ignoré : privilège manquant
[14:32:02] [process]   ✓ 214 processus, 3 accès refusés (protégés)          (1 284 ms)
[14:32:04] [usn]       ✓ C: — 184 213 enregistrements, 14/08 → 28/08        (2 641 ms)
[14:32:04] [usn]       ✕ D: — volume exFAT, journal USN non disponible
[14:32:08] [registry]  ✓ 6 artefacts, 412 valeurs lues                        (711 ms)
[14:32:11] [prefetch]  ⚠ 318/402 fichiers lus, 84 verrouillés                (2 903 ms)
[14:32:27] [scan]      Scan terminé — 41 812 éléments — 12 détections — score 72/100
```

Deux journaux : **exécution** (ci-dessus) et **accès** (chaque clé, chemin et PID réellement lus).
Le second est la garantie de transparence : n'importe qui peut vérifier a posteriori ce que le programme a
touché. Aucun contenu sensible n'est journalisé.

---

## 4. Gestion d'erreurs (§25)

Trois états de première classe, jamais une exception qui remonte : `✓ Réussi · ⚠ Partiel · ✕ Impossible`.

| Situation | Traitement |
|---|---|
| Accès refusé (fichier, clé, processus) | `Diagnostic` avec la ressource et le code Win32 ; le module continue ; statut `Partiel` |
| Fichier ou clé inexistant | Normal, pas une erreur : `Diagnostic` de niveau `Info` |
| Processus terminé pendant le scan | Attendu ; l'entrée est marquée « processus disparu en cours d'analyse » |
| Volume non NTFS | Module USN `Impossible` sur ce volume, poursuite sur les autres |
| Journal USN indisponible ou désactivé | `Impossible` + motif explicite ; **et** observation transmise à M18 |
| Antivirus bloquant une lecture | Détecté via le code d'erreur, mentionné dans le rapport comme angle mort |
| Timeout d'un module (défaut 180 s) | Résultats partiels conservés, module marqué `Partiel (délai dépassé)` |
| Exception inattendue | Capturée par `ModuleHost`, type et pile enregistrés, **le scan continue toujours** |

Le processus hôte installe des gestionnaires `AppDomain.UnhandledException`,
`TaskScheduler.UnobservedTaskException` et `DispatcherUnhandledException` : dans le pire des cas, l'outil
propose l'export du rapport partiel plutôt que de disparaître.

**Principe** : un module qui échoue produit un résultat qui décrit son échec. L'absence d'information est
elle-même une information à afficher au staff, jamais un blanc silencieux.

---

## 5. Performance (§26)

| Levier | Application |
|---|---|
| Aucun parcours de disque complet | Racines configurées, profondeur maximale, filtrage d'extension, exclusions |
| API Windows plutôt que processus externes | Aucun appel à `fsutil`, `findstr`, `reg`, `wevtutil`, `powershell` |
| Lecture USN en une passe | Buffers de 1 Mo, table MFT construite une seule fois pour la résolution des chemins |
| Hash mémorisé | `FileFactsCache` partagé : un fichier n'est jamais hashé ni vérifié deux fois |
| Hash plafonné | Au-delà de la taille configurée (512 Mo), métadonnées seules |
| Parallélisme mesuré | 4 modules simultanés — la charge est I/O disque ; davantage dégrade sur HDD |
| Requêtes Event Log ciblées | XPath + fenêtre temporelle, jamais de lecture intégrale des journaux |
| Mémoire bornée | Plafond par processus, lecture par blocs, régions non lisibles ignorées |
| UI virtualisée | Listes de dizaines de milliers de lignes sans dégradation |

Objectifs indicatifs sur un poste courant (SSD, 8 cœurs) : profil `Rapide` < 30 s ·
`Standard` 2 – 4 min · `Approfondi` 5 – 10 min (dominé par l'analyse mémoire).

---

**Étape suivante** : `docs/06-plan-developpement.md`.
