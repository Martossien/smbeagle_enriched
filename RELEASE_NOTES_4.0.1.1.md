# SMBeagle_enriched v4.0.1.1 - Release Production

## 🚀 Nouveautés Majeures
Cette version enrichit SMBeagle avec **6 nouvelles métadonnées critiques** :

### ✅ Nouvelles Options CLI
- `--sizefile` : Collecte taille fichiers en bytes
- `--access-time` : Collecte derniers temps d'accès
- `--fileattributes` : Collecte attributs système Windows
- `--ownerfile` : Collecte propriétaire (DOMAIN\Username)
- `--fasthash` : Empreinte xxHash64 rapide (64KB)
- `--file-signature` : Détection type par magic bytes

### 📊 CSV Output Enrichi
Nouvelles colonnes disponibles :
`FileSize`, `AccessTime`, `FileAttributes`, `Owner`, `FastHash`, `FileSignature`

### 🎯 Usage Production
```bash
# Scan complet avec toutes les métadonnées
SMBeagle --sizefile --access-time --fileattributes --ownerfile --fasthash --file-signature -c enriched_scan.csv -D -v

# Usage Brique 1 (pipeline 3 briques)
SMBeagle -l -c brique1_output.csv --sizefile --ownerfile --fasthash --access-time --fileattributes --file-signature
```

### Installation

Téléchargez l'archive pour votre plateforme
Extrayez l'exécutable
Lancez directement (self-contained, pas de dépendances)

🎯 **Focus Windows x64**
Version optimisée pour serveurs de fichiers Windows offline.
Idéal pour scan local avec métadonnées enrichies.

🐛 **Corrections**

Compilation .NET 9.0 optimisée
Gestion d'erreurs améliorée
Performance hash et signatures

⚠️ **Breaking Changes**
Aucun - compatible avec toutes les options SMBeagle existantes.
