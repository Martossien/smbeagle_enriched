# Implementation Report --local-path

## Approche Architecturale Choisie
- [x] Extension FileFinder avec objets dummy Share/Host
- [ ] Nouveau constructeur FileFinder
- La fonctionnalité --local-path s'appuie sur des répertoires locaux représentés par un Share "LOCAL_SCAN" associé à l'hôte fictif "localhost". Les méthodes de FileFinder détectent ce mode et utilisent des appels système locaux.

## Modifications Détaillées Par Fichier
- **Program.cs** : ajout de l'option `local-path` et traitement dédié avant toute découverte réseau.
- **FileFinder.cs** : prise en charge du mode local via `_localScan`, création de répertoires locaux, nouvelles méthodes `FetchFilePermissionLocal` et `FetchFileLocal`.
- **Directory.cs** : adaptation de `UNCPath`, ajout des méthodes `FindFilesLocal` et `FindDirectoriesLocal`, prise en charge du mode local dans les récursions.
- **LocalHelper.cs** : nouveaux utilitaires cross‑platform pour le calcul de hash, la détection de signature et la résolution de permissions locales.

## Tests de Validation Réalisés
- `dotnet build` *(échec : SDK .NET 9 manquant)*
- `dotnet run -- --help` *(non exécuté à cause de l'échec build)*

## Performance et Comportement
- Le scan local évite toute latence réseau et utilise `System.IO` pour l'énumération.

## Recommandations Futures
- Installer le SDK .NET 9 pour pouvoir compiler et tester intégralement.
- Étendre la résolution des ACLs pour Linux via `Mono.Posix`.
- Ajouter des tests unitaires sur les nouvelles fonctionnalités.
