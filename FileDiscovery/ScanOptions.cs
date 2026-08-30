using SMBeagle.ShareDiscovery;
using System.Collections.Generic;

namespace SMBeagle.FileDiscovery
{
    /// <summary>
    /// Options effectives d'une énumération de fichiers. Remplace la quinzaine de
    /// paramètres positionnels de <see cref="FileFinder"/> et circule jusqu'aux
    /// méthodes de <see cref="Directory"/>.
    /// </summary>
    class ScanOptions
    {
        public List<Share> Shares { get; init; } = new();
        public List<string> LocalPaths { get; init; } = new();
        public string OutputDirectory { get; init; } = "loot";
        public bool FetchFiles { get; init; }
        public List<string> FilePatterns { get; init; } = new();
        public bool GetPermissionsForSingleFileInDir { get; init; } = true;
        public bool EnumerateAcls { get; init; } = true;
        public bool Quiet { get; init; }
        public bool Verbose { get; init; }
        public bool CrossPlatform { get; init; }
        public bool IncludeFileSize { get; init; }
        public bool IncludeAccessTime { get; init; }
        public bool IncludeFileAttributes { get; init; }
        public bool IncludeFileOwner { get; init; }
        public bool IncludeFastHash { get; init; }
        public bool IncludeFileSignature { get; init; }
        /// <summary>Restaurer la date de dernier accès après lecture du contenu.</summary>
        public bool PreserveAccessTime { get; init; }

        public bool IsLocalScan => LocalPaths.Count > 0;

        /// <summary>Vrai si une option impose de lire le contenu des fichiers.</summary>
        public bool ReadsContent => IncludeFastHash || IncludeFileSignature;
    }
}
