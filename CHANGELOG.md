# Changelog

Toutes les modifications notables de SMBeagle_enriched sont documentées ici. Le format suit
[Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) ; les numéros suivent
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) : une version **mineure** quand le
contrat lu par docia (CSV, manifeste, codes de retour) s'étend, un **correctif** sinon.
Les comptes rendus de sessions antérieurs à ce fichier sont dans `Docs/archive/`.

## [4.4.0] — 2026-09-02

### Modifié

- **Examen des fichiers en parallèle au sein d'un répertoire** (`--file-workers`, défaut 8,
  1 = comportement d'avant) : propriétaire, droits, empreinte et signature coûtent chacun un
  à quatre allers-retours vers le serveur de fichiers ; ils sont menés de front, l'ordre du
  CSV reste celui de l'énumération. Un fichier qui disparaît ou se refuse en cours d'examen
  est compté (`counts.files_unreadable` dans le manifeste, `-v` pour le détail) et sauté au
  lieu de faire tomber tout le répertoire.
- **Propriétaire Linux mis en cache** (uid/gid → nom) : la résolution était refaite pour
  chaque fichier (0,85 ms, 17 s sur 20 000 fichiers pour trois comptes distincts).
- **Propriétaire Windows en un seul appel** `GetFileSecurity` dans le cas courant (tampon
  fixe, second appel seulement si le descripteur est plus grand) au lieu de deux par fichier.

## [4.3.0] — 2026-09-02

### Corrigé

- **Une racine de plus de 20 sous-répertoires sortait en code 3 « aucun fichier »** avec un
  manifeste sans cible, toutes ses lignes pourtant écrites : les cibles étaient lues après le
  découpage des grandes racines en lots (`SplitLargeDirectories`), qui remplace la racine par
  ses enfants. Mesuré sur 100 000 fichiers : exit 3, `targets: []`. Les cibles sont lues
  avant le découpage ; code 0 et `targets` corrects.
- **Un sous-répertoire fermé par ACL disparaissait sans un mot** hors `-v` : l'inventaire se
  présentait comme complet. Compté (`counts.dirs_unreadable`), listé
  (`unreadable_directories`, 200 chemins au plus) et résumé sur stderr en fin de scan ; le
  scan sort en 0 — ce n'est pas une cible écartée —, docia relaie l'information.
- **Une jonction ou un lien de répertoire vers un ancêtre faisait boucler l'énumération**
  jusqu'au « chemin trop long » : les points de réanalyse sont ignorés et comptés
  (`counts.reparse_points_skipped`) ; leur contenu réel est scanné par son vrai chemin.
- Un `--local-path` mal formé (chemin relatif, fragment) n'est plus résolu contre le
  répertoire courant et scanné en silence : refus explicite en code 2.
- Sans `--sizefile`, la colonne `FileSize` est **vide**, plus `0` — docia excluait tout le
  partage « fichier trop petit ».
- `-q` fait taire le rappel « Will NOT Grab files » (port de l'amont #99).

### Ajouté

- **Code de retour 4 « périmètre incomplet »** : une cible demandée mais non scannée (ACL,
  montage cassé) est nommée dans `skipped` du manifeste ; le CSV reste exploitable.

### Modifié

- Les fichiers de chaque répertoire sont écrits **dès leur énumération** puis oubliés, au lieu
  de garder l'arbre entier jusqu'à la fin (100 000 fichiers : 169 → 133 Mo, CSV identique).
- Les 17 comptes rendus de sessions (`*_REPORT.md`, notes 4.0.1.1) quittent la racine pour
  `Docs/archive/` ; ce fichier devient l'historique de référence.

### Technique

- Trois tests bout en bout (racine de 25 sous-dossiers, dossier illisible, lien vers le
  parent) ; contrat du manifeste vérifié ; 47 tests.

## [4.2.0] — 2026-08-30

Première release du fork sous ce nom : `--local-path`, six métadonnées (`--sizefile`,
`--access-time`, `--fileattributes`, `--ownerfile`, `--fasthash`, `--file-signature`),
`--preserve-access-time`, `--progress-json`, `--manifest`, codes de retour 0/1/2/3, binaires
autonomes win-x64 et linux-x64, CI et test de contrat CSV relu par docia.
