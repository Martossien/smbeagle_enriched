# SMBeagle_enriched

[![CI](https://github.com/Martossien/smbeagle_enriched/actions/workflows/ci.yml/badge.svg)](https://github.com/Martossien/smbeagle_enriched/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Plateformes](https://img.shields.io/badge/plateformes-Windows%20%7C%20Linux-lightgrey.svg)](https://github.com/Martossien/smbeagle_enriched/releases)
[![Licence](https://img.shields.io/badge/licence-Apache%202.0-blue.svg)](LICENSE)

SMBeagle_enriched est un fork de [SMBeagle](https://github.com/punk-security/smbeagle) (Punk Security) qui
ajoute la collecte de métadonnées de fichiers, le scan d'un dossier local et tout ce qu'il faut pour être
piloté en sous-processus par l'analyseur documentaire [docia](https://github.com/Martossien/llm-content-analyzer) :
progression JSON, manifeste de scan, codes de retour nets et préservation des dates d'accès.
Toute l'énumération SMB, le contrôle des permissions et la sortie CSV / Elasticsearch viennent du projet amont.

## Ce que le fork ajoute

| Option | Rôle | Windows | Linux |
|--------|------|---------|-------|
| `--local-path <dossier>` (répétable) | Scanner des dossiers locaux au lieu du réseau SMB | oui | oui |
| `--sizefile` | Taille en octets | oui | oui |
| `--access-time` | Date de dernier accès | oui | oui |
| `--fileattributes` | Attributs du système de fichiers | complet | basique |
| `--ownerfile` | Propriétaire (`DOMAINE\utilisateur`, ou `utilisateur:groupe` en local Linux) | oui | local uniquement, `<NOT_SUPPORTED>` en SMB |
| `--fasthash` | xxHash64 des 64 premiers Ko | oui | oui |
| `--file-signature` | Type détecté par nombres magiques et structure de l'en-tête (64 premiers Ko) | oui | oui |
| `--preserve-access-time` | Remet la date d'accès après lecture du contenu | oui | oui (voir limites) |
| `--progress-json` | Progression JSON sur stdout | oui | oui |
| `--manifest <fichier.json>` | Manifeste de scan en fin d'exécution | oui | oui |

## Installation

Prérequis : .NET 9 SDK pour compiler ; les binaires publiés sont autonomes (aucun runtime à installer).

```bash
git clone https://github.com/Martossien/smbeagle_enriched.git
cd smbeagle_enriched
dotnet build -c Release            # exécutable dans bin/Release/net9.0/<rid>/
scripts/check.sh                   # build sans avertissement, format, tests, contrat docia
```

Binaires autonomes (un seul fichier) comme en CI :

```bash
dotnet publish SMBeagle.csproj -c Release --self-contained -r win-x64   -o packages/win-x64   -p:PublishSingleFile=true -p:PublishTrimmed=false -p:InvariantGlobalization=true
dotnet publish SMBeagle.csproj -c Release --self-contained -r linux-x64 -o packages/linux-x64 -p:PublishSingleFile=true -p:PublishTrimmed=false -p:InvariantGlobalization=true
```

Les artefacts `windows-x64` et `linux-x64` de chaque exécution CI sont téléchargeables depuis l'onglet Actions.
Une image Docker se construit depuis les sources : `docker build -t smbeagle .`

## Utilisation

### Mode standard : scan local sur un poste Windows (`--local-path`)

C'est le mode utilisé et validé pour l'audit Doc-IA : SMBeagle tourne sur un poste Windows et scanne un dossier
ou un **lecteur réseau mappé** (`D:\partage`, `Z:\`). Propriétaires (`--ownerfile`), attributs (`--fileattributes`)
et dates sont lus via les API Win32, sans identifiants SMB.

### Appel « pour docia » (scan local, toutes les métadonnées)

```cmd
SMBeagle.exe --local-path "D:\partage" --sizefile --access-time --fileattributes --ownerfile --fasthash --file-signature --preserve-access-time --progress-json --manifest scan.json -c scan.csv
```

docia lit les lignes JSON sur stdout, le CSV et le manifeste à la fin, et se fie au code de retour.
Le même appel fonctionne sous Linux (`./SMBeagle --local-path /srv/partage ...`), sans identifiants.

> **Chemins contenant une espace : guillemets obligatoires.** `--local-path D:\mes fichiers` sans guillemets
> arrive en deux arguments et SMBeagle refuse alors de scanner (code 2). Écrire `--local-path "D:\mes fichiers"`,
> et **ne pas terminer un chemin guillemeté par un antislash** : `"D:\dossier\"` ne ferme pas la citation
> (règle MSVCRT) — écrire `"D:\dossier"`.

### Scan réseau SMB (fonctionnalité amont, non validée pour l'audit)

Disponible mais **pas validé pour l'audit Doc-IA** : découverte réseau, `--host` / `--share` avec identifiants
(SMBLibrary). Le contrat CSV est le même, mais `--ownerfile` rend `<NOT_SUPPORTED>` en cross-platform et les
dates SMB sont en UTC.

```bash
# Windows, authentification intégrée
SMBeagle.exe -c resultats.csv --sizefile --access-time --fasthash --file-signature

# Linux ou identifiants explicites (SMBLibrary)
./SMBeagle -c resultats.csv -u utilisateur -p motdepasse -d DOMAINE -n 192.168.1.0/24 --sizefile --access-time
```

Les options amont restent inchangées (`-n`, `-h`, `-N`, `-H`, `-s`, `-S`, `-E`, `-D`, `-A`, `-f`, `-g`, `--loot`,
`--file-pattern`, `-e`, `-q`, `-v`...) : `SMBeagle --help` les liste toutes. `-a` / `--aggression` (1 à 10, défaut 6)
règle le délai de sonde TCP 445 : `1010 - 100 × a` ms par hôte (commit amont #100).

### Scan de dossiers locaux

```bash
SMBeagle --local-path /home/data -c scan.csv
SMBeagle --local-path /var/log /opt/data --sizefile --fasthash -c scan.csv
SMBeagle --local-path "/srv/mes fichiers" -c "/srv/rapports/scan du jour.csv"
```

`--local-path` est exclusif des options réseau (elles sont ignorées avec un avertissement).

Chaque chemin doit être **absolu** et exister, sinon SMBeagle s'arrête en code 2 **avant tout scan** :

- un chemin relatif (`../data`, `fichiers`, `C:foo`) est refusé au lieu d'être résolu contre le répertoire
  courant. C'est ce qui faisait scanner le mauvais dossier en silence quand Windows coupait
  `--local-path D:\mes fichiers` en deux arguments : le fragment `fichiers` existe souvent dans le répertoire
  courant (`Documents`, `Downloads`, `Bureau`...) et passait alors pour une cible valide ;
- un chemin inexistant ou injoignable (lecteur mappé déconnecté, partage absent) est refusé, avec le motif ;
- une valeur vide (`--local-path ""`) est refusée avec un message explicite.

En revanche, **un chemin qui existe mais dont l'accès est refusé n'est pas une erreur d'arguments** :
il est écarté avec un avertissement (`WARNING: --local-path access denied, directory skipped`) et le scan
continue sur les autres chemins, comme le fait déjà l'énumération pour un sous-dossier fermé. Si plus aucun
chemin n'est exploitable, le scan se termine normalement en code 3. C'est le cas courant d'un partage
partiellement fermé par ACL : l'audit doit continuer, pas échouer.

## Codes de retour

| Code | Signification |
|------|---------------|
| 0 | Scan terminé, au moins un fichier écrit |
| 1 | Erreur d'exécution : exception, fichier CSV impossible à créer, interruption CTRL-C |
| 2 | Arguments invalides : option inconnue, argument surnuméraire (chemin non guillemeté), chemin `--local-path` relatif, vide, inexistant ou injoignable, `-c` vide, aucune sortie (`-c` et/ou `-e`), identifiants incomplets (ou absents hors Windows en mode réseau), `-l` avec identifiants, `-a` hors 1..10, motif `--file-pattern` invalide |
| 3 | Rien trouvé : aucun chemin local exploitable (tous les `--local-path` en accès refusé), aucun hôte / partage accessible, zéro fichier |

`--help` et `--version` rendent 0. Avec le code 3, le CSV (vide, ou réduit à son en-tête), le manifeste et
l'événement `done` sont quand même produits : un appelant comme docia peut relire le CSV et poursuivre.

## Progression JSON (`--progress-json`)

Avec cette option, **stdout ne contient que des lignes JSON** (une par ligne, UTF-8) ; la sortie lisible
(logo, étapes, erreurs) part sur stderr. Sans l'option, rien ne change.

```json
{"event":"progress","stage":"discovery","hosts":0,"shares":0,"files":0,"elapsed_s":0.1}
{"event":"progress","stage":"shares","hosts":3,"shares":0,"files":0,"elapsed_s":4.6}
{"event":"progress","stage":"files","hosts":2,"shares":5,"files":1240,"elapsed_s":12.0}
{"event":"progress","stage":"writing","hosts":2,"shares":5,"files":48213,"elapsed_s":310.4}
{"event":"done","files":48213,"csv":"C:\\scans\\scan.csv","elapsed_s":310.9}
```

- `progress` est émis à chaque changement d'étape et toutes les ~2 s. Étapes : `discovery` (réseau et hôtes),
  `shares`, `files` (énumération des répertoires puis des fichiers), `writing` (vidage des sorties).
  En `--local-path`, le scan commence directement à `files` et `hosts` / `shares` valent 0.
- `done` clôt le scan (code 0 ou 3) : `files` est le nombre de fichiers écrits, `csv` le chemin absolu du CSV
  (`null` si seule la sortie Elasticsearch est active).
- `error` remplace `done` en cas d'échec (code 1 ou 2) : `{"event":"error","message":"...","elapsed_s":0.3}`.
  Une erreur d'arguments produit un unique événement `error`.
- `elapsed_s` est en secondes, arrondi au dixième.

## Manifeste (`--manifest chemin.json`)

Écrit en fin de scan (codes 0 et 3), JSON indenté, UTF-8 :

```json
{
  "version": "4.2.0",
  "started_at": "2026-08-30T19:49:03.1328939+02:00",
  "finished_at": "2026-08-30T19:49:03.2996582+02:00",
  "options": {
    "csv-file": "C:\\scans\\scan.csv",
    "elasticsearch-host": null,
    "elasticsearch-port": "9200",
    "local-path": ["D:\\partage"],
    "network": [],
    "host": [],
    "username": null,
    "password": null,
    "sizefile": true,
    "access-time": true,
    "fasthash": true,
    "preserve-access-time": true,
    "progress-json": true,
    "manifest": "C:\\scans\\scan.json",
    "...": "toutes les autres options, clé = nom long, valeur effective (défauts compris)"
  },
  "targets": ["D:\\partage"],
  "counts": { "hosts": 0, "shares": 0, "files": 48213 },
  "csv": "C:\\scans\\scan.csv",
  "columns": ["Name", "Host", "Extension", "Username", "Hostname", "UNCDirectory", "CreationTime", "LastWriteTime", "Readable", "Writeable", "Deletable", "DirectoryType", "Base", "FileSize", "AccessTime", "FileAttributes", "Owner", "FastHash", "FileSignature"]
}
```

- `options` : chaque option de la ligne de commande avec sa valeur effective ; le mot de passe est masqué (`"***"`).
- `targets` : chemins locaux validés (absolus) ou, en réseau, réseaux et hôtes retenus après filtrage.
- `counts.hosts` / `counts.shares` : hôtes avec partages et partages scannés (0 en local) ; `counts.files` : fichiers écrits.

## Contrat CSV (19 colonnes)

Le CSV est celui consommé par docia (`src/docia/ingest/smbeagle_csv.py`). Il est figé et couvert par des tests :

```
Name,Host,Extension,Username,Hostname,UNCDirectory,CreationTime,LastWriteTime,Readable,Writeable,Deletable,DirectoryType,Base,FileSize,AccessTime,FileAttributes,Owner,FastHash,FileSignature
"rapport financier 2024.pdf","localhost","pdf","admin","poste.WORKGROUP","D:\partage\compta",30/08/2026 19:31:14,30/08/2026 19:31:14,True,True,True,LOCAL_FIXED,"\\localhost\LOCAL_SCAN\",51,30/08/2026 19:31:56,"Archive","DOM\alice","359ac996b591b861","pdf"
```

- Guillemets sélectifs : les colonnes texte (`Name`, `Host`, `Extension`, `Username`, `Hostname`, `UNCDirectory`,
  `Base`, `FileAttributes`, `Owner`, `FastHash`, `FileSignature`) sont entre guillemets, un guillemet interne est
  doublé (`""`, RFC 4180) ; les DateTime, booléens, entiers et énumérations sont nus.
- Dates au format fixe `dd/MM/yyyy HH:mm:ss`, quelle que soit la culture de la machine (**changement depuis
  4.0.1.1**, qui suivait la culture du poste : `M/d/yyyy h:mm:ss tt` en en-US, `MM/dd/yyyy` en build invariante).
- `Extension` en minuscules sans point ; `Name` et `UNCDirectory` conservent la casse.
- En `--local-path` : `Host` vaut `localhost`, `Base` vaut `\\localhost\LOCAL_SCAN\`, `DirectoryType` vaut `LOCAL_FIXED`,
  `UNCDirectory` est le chemin absolu du dossier.
- `FastHash` : xxHash64 (16 caractères hexadécimaux) des 64 premiers Ko ; `FileSignature` : extension détectée
  sur ces mêmes 64 Ko (`pdf`, `png`, `doc`, `xls`, `docx`, ...), `ole` pour un fichier composé OLE2 (doc/xls/ppt)
  dont la structure dépasse l'en-tête lu, ou `unknown`. Si le fichier n'est pas lisible, les deux valeurs sont
  **vides** (docia exclut un `FastHash` vide de ses familles de doublons) et l'erreur est journalisée sur stderr
  en `-v` ; un échec de la seule détection de signature ne vide jamais le hash.
- Une option non demandée laisse sa colonne à sa valeur neutre (`0`, date `01/01/0001 00:00:00`, chaîne vide).

Le CSV d'or `tests/golden/scan_19col.csv` est produit par un scan de `tests/fixtures` ; `scripts/check.sh` le
relit avec le parseur docia (import strict sur une base temporaire) quand le venv docia est présent.

## Dates d'accès

`--fasthash` et `--file-signature` lisent le début de chaque fichier, ce qui peut mettre à jour sa date de dernier
accès. La date est toujours lue **avant** cette lecture, donc la colonne `AccessTime` reflète l'accès réel
précédent. `--preserve-access-time` remet en plus l'horodatage d'origine après lecture, pour que les scans
successifs et les statistiques « fichiers non accédés depuis X ans » restent justes :

- fichier local : `File.SetLastAccessTimeUtc` (Windows : droit d'écrire les attributs ; Linux : il faut être
  propriétaire du fichier ou root, sinon `EPERM`) ;
- SMB avec identifiants ou depuis Linux (SMBLibrary) : ouverture avec `FILE_WRITE_ATTRIBUTES` puis
  `SetFileInformation(FileBasicInformation.LastAccessTime)` sur le handle avant fermeture ; sans ce droit le
  fichier est quand même lu ;
- SMB en authentification intégrée Windows : les fichiers sont ouverts par chemin UNC, même mécanisme que le local.

Les échecs sont comptés et résumés sur stderr en fin de scan (`-v` pour le détail), **jamais bloquants** : sur un
lecteur mappé en lecture seule, la restauration est impossible (droit `FILE_WRITE_ATTRIBUTES` requis) mais la
colonne `AccessTime` reste juste puisqu'elle est lue avant la lecture, et docia conserve de son côté la première
date d'accès observée. Sur des volumes NTFS où la mise à jour des dates d'accès est désactivée
(`NtfsDisableLastAccessUpdate`), l'option est sans effet.

## Limites connues

- `--ownerfile` en SMB cross-platform (Linux ou identifiants explicites) rend `<NOT_SUPPORTED>`.
- Le mode SMB cross-platform (Linux ou identifiants explicites) n'est pas validé pour l'audit ; il n'est pas
  couvert par la CI (pas de serveur SMB), seulement vérifié à la main sur un Samba de test. `--preserve-access-time`
  y est implémenté via SMBLibrary. En local il est testé sous Linux et Windows.
- Le hash et la signature portent sur les 64 premiers Ko : deux fichiers de même en-tête et de même taille
  forment une famille de doublons dans docia sans être forcément identiques.
- Elasticsearch n'est pas testé par la CI.

## Développement

- `scripts/check.sh` doit afficher `VERDICT: OK` avant tout push : build Release `-warnaserror`,
  `dotnet format --verify-no-changes`, `dotnet test`, contrat docia si `DOCIA_PYTHON` (ou le venv local) existe.
- Tests : `tests/SMBeagle.Tests` (xunit) — formateur CSV en mémoire, scans bout en bout de l'exécutable en
  `--local-path`, hash/signature, codes de retour, progression JSON, manifeste, dates d'accès.
- CI GitHub (`.github/workflows/ci.yml`) : build, format, tests et publication des binaires sur
  `ubuntu-latest` et `windows-latest`. Ce qui est propre à Windows (propriétaire, attributs Win32) est couvert
  par le job Windows.

## Licence et remerciements

Apache License 2.0, comme SMBeagle. Merci à l'équipe Punk Security pour l'outil d'origine :
https://github.com/punk-security/smbeagle
