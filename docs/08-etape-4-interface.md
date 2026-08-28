# Étape 4 — Interface

**Résultat** : 4 écrans WPF en MVVM, thème sombre, navigation complète, export JSON et TXT
fonctionnels. `dotnet build` à 0 avertissement, **59 tests verts**.

---

## 0. Direction artistique

Le scanner et le lecteur de rapport reprennent la D.A. **Solve Community** : fond navy très sombre,
accent bleu électrique, cartes arrondies, en-têtes de section en petites capitales espacées,
typographie entièrement sans serif. Les jetons de couleur sont identiques des deux côtés
(`src/GModForensic.App/Themes/Dark.xaml` et `tools/report-reader.html`) — les deux produits doivent
se ressembler.

| Rôle | Sombre | Clair |
|---|---|---|
| Fond / surface | `#080D18` / `#0E1626` | `#F3F6FC` / `#FFFFFF` |
| Accent | `#3B82F6` | `#2563EB` |
| Texte / secondaire | `#E6EDF9` / `#94A5C4` | `#0C1424` / `#47587A` |

**Le bleu d'accent ne code jamais une gravité.** La gravité est une échelle *ordinale*, traitée comme
telle : sa clarté progresse de façon monotone (`L` 0,68 → 0,75 → 0,83 en sombre, 0,48 → 0,56 → 0,63
en clair), ce qui la rend lisible sous toute forme de daltonisme, et « faible » est un neutre placé
hors de la rampe chaude. Chaque gravité porte en plus son libellé texte : la couleur n'est jamais
seule porteuse d'information.

Cette rampe corrige un défaut réel de la version précédente, mesuré par le validateur de palette :
« élevé » et « moyen » n'étaient séparés que de ΔE 8,8 pour un seuil de 15 — indistinguables même en
vision normale.

---

## 1. La décision qui rend l'interface testable

WPF ne s'exécute que sous Windows. Une interface écrite entièrement dans le projet `App` aurait donc
été **invérifiable en intégration continue**, et invérifiable ici.

D'où un huitième projet, `GModForensic.Presentation`, en `net8.0` :

```
GModForensic.Presentation   (net8.0)          ViewModels — AUCUNE référence à WPF
GModForensic.App            (net8.0-windows)  XAML, thème, câblage Windows uniquement
```

Les ViewModels n'exposent jamais une couleur, jamais un `Visibility`, jamais un `Brush` : ils
exposent un **sens** (`Tone = "crit"`, `StatusText`, `EmptyMessage`), que des convertisseurs
traduisent côté XAML. Conséquence directe : **toute la logique d'interface est testée sur Linux** —
navigation, blocage du démarrage sans consentement, annulation en cours de scan, filtres, score,
export et gestion d'un échec d'écriture.

Le projet `App` ne contient plus que du XAML, trois convertisseurs et 60 lignes de câblage.

---

## 2. Les quatre écrans

### Accueil — ce qui ne pourra pas être vérifié, dit avant le scan

Le §2 demande d'annoncer les vérifications impossibles. L'écran le fait à deux niveaux : les
privilèges mesurés (`CapabilityProbe`), puis la liste des modules qui seront ignorés **avec leur
motif exact**, sans avoir à dérouler la liste complète.

Le démarrage est bloqué tant que le consentement, le nom du staff et l'identifiant du sujet ne sont
pas renseignés. Ce n'est pas une option : l'outil lit des artefacts détaillés sur une machine
personnelle, et ces trois éléments sont inscrits au rapport.

### Scan — progression, états, journal

Barre pondérée par module, étape courante, éléments analysés, temps écoulé, et la liste des modules
avec leurs cinq états (`✓ ⚠ ✕ ○ ⊘`). Le journal d'exécution défile à droite.

Le compteur d'indicateurs affiche **« 7 (provisoire) »**, jamais « 7 » : la corrélation fusionne
ensuite des observations en détections composites, le chiffre définitif ne peut donc pas être connu
pendant le scan. Le libellé le dit plutôt que de laisser croire à un total.

### Résultats — le score s'explique, toujours

Score, bande d'interprétation, répartition par gravité, et la clause fixe non masquable.
Liste filtrable par gravité, catégorie et recherche plein texte ; virtualisée pour tenir des dizaines
de milliers de lignes.

Le panneau de détail affiche, pour chaque détection : l'explication en français, le **détail ligne à
ligne du score** (§21), les **causes légitimes possibles** (§22), l'élément concerné avec son
horodatage et sa source, et la **preuve brute** avec son indication de vérification manuelle.

### Export — JSON et TXT

Dossier de sortie, choix des formats, chemins écrits. Le rapport HTML autonome — celui que lira le
staff — reste l'objet de l'étape 8, et l'écran le dit au lieu de proposer une case inerte.

Un échec d'écriture (disque plein, dossier protégé) est signalé sans jamais faire perdre les
résultats du scan : l'export reste retentable ailleurs.

---

## 3. Ce que l'écran de résultats affiche réellement

Sortie de `dotnet test --filter Walkthrough --logger "console;verbosity=detailed"` :

```
  Score de suspicion : 90 / 100   —   Indicateurs tres eleves
  Faisceau d'indicateurs concordants — verification manuelle prioritaire

  Critiques : 2    Eleves : 3    Moyens : 2    Faibles : 5

  Contribution par categorie (plafonnee a 60) :
    UsnJournal          60      Prefetch            10
    EventLog            50      AntiForensic        10
    Downloads           21      FileSystem           4
    RemovableDevices    12      RecentFiles          2

DETAIL DE LA DETECTION SELECTIONNEE
  Executable lance puis efface   [CRITIQUE]   Confiance elevee

  Pourquoi ce score :
      15  Trace d'execution trouvee dans le Prefetch
      15  Suppression confirmee par le journal USN
      20  Renomme « cheat_loader.exe » -> « WindowsUpdate.exe »
      10  Situe dans un dossier temporaire utilisateur
    Total : 60/100

  Causes legitimes possibles :
    Un desinstalleur ou un installeur temporaire produit la meme sequence.
    Verifier le nom d'origine et l'editeur avant toute conclusion.
```

---

## 4. Deux briques avancées, et pourquoi

L'étape 4 était prévue « interface seule », avec un écran de résultats sur données de démonstration.
Deux éléments ont été avancés parce qu'un écran de résultats sans score et un bouton d'export inerte
n'auraient rien prouvé :

| Avancé | Ce qui reste à son étape |
|---|---|
| **`ScoreAggregator`** (étape 7) — la formule était déjà entièrement spécifiée dans `docs/04` | Étape 7 : base de connaissance, atténuateurs, calibrage sur corpus réel |
| **Export JSON et TXT** (étape 8) | Étape 8 : rapport **HTML autonome**, chronologie unifiée, échappement strict |

Le scoring est vérifié par sept tests qui reprennent les propriétés annoncées dans `docs/04` :

| Propriété | Vérifié |
|---|---|
| Un seul indicateur critique ne sature jamais le score | 50/100, pas 100 |
| Deux indicateurs pèsent plus qu'un, moins que leur somme | 30 → 51, pas 60 |
| Une confiance faible réduit réellement la contribution | 30 → 12 |
| Cent détections d'une même règle ne saturent pas | < 25/100 |
| Une seule catégorie ne peut pas dépasser son plafond | 60 max |
| Aucune bande ne parle de triche | assertion sur les libellés |

---

## 5. Un défaut trouvé pendant l'étape

Les modules de démonstration ne produisaient **aucune observation** : le moteur de détection n'avait
donc rien à évaluer et l'écran de résultats restait vide. Six tests ont échoué d'un coup, tous pour
cette cause unique.

La correction est à la racine plutôt que dans les tests : les modules de démonstration émettent
maintenant de vraies `Observation`, exactement dans la forme qu'auront celles des modules réels. Le
compteur « indicateurs (provisoire) » de l'écran de scan devient du même coup significatif.

---

## 6. Limite de vérification — à lire

**L'interface n'a pas pu être vue.** WPF ne s'exécute pas sur Linux ; seule la compilation du XAML
(génération BAML) a été vérifiée, ainsi que la totalité de la logique via les 59 tests.

Ce qui est donc **prouvé** : la logique des quatre écrans, la navigation, l'annulation, les filtres,
le score, l'export, et le fait que le XAML compile.

Ce qui reste **à vérifier sur une machine Windows** : le rendu visuel, l'alignement des colonnes, le
comportement des `ScrollViewer`, la lisibilité réelle du thème sombre et le confort d'usage. Les
styles ont été gardés volontairement conventionnels pour limiter les surprises, mais un passage sur
Windows est nécessaire avant de considérer l'étape close.

---

## 7. Point ouvert : calibrage du score

Le jeu de démonstration atteint **90/100**. Le calcul est conforme à la formule spécifiée, et ce jeu
est délibérément chargé (deux critiques dont un blocage CodeIntegrity, plus trois élevés). Reste que
le **calibrage** — à partir de quelle accumulation d'indicateurs faibles le score doit-il monter —
ne peut se régler que sur le corpus de machines saines de l'étape 7. À considérer comme provisoire.
