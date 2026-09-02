using SMBeagle.ShareDiscovery;
using SMBeagle.Output;
using SMBLibrary;
using SMBLibrary.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SMBeagle.FileDiscovery
{
    class Directory
    {
        public Share Share { get; set; }
        public string Path { get; set; }
        public string UNCPath
        {
            get
            {
                // For local scans return the path directly
                if (Share != null && Share.Name == "LOCAL_SCAN")
                    return Path;
                // Windows enum needs UNC Paths as Path but Cross-platform doesnt.
                if (Path.StartsWith(@"\\"))
                    return Path;
                else
                    return $"{Share.uncPath}{Path}";
            }
        }
        //todo: replace Base and Type with direct copy from parent then drop the ref
#nullable enable
        public Directory? Parent { get; set; } = null;
#nullable disable
        public Directory Base
        {
            get
            {
                if (Parent == null)
                    return this;
                else
                    return Parent.Base;
            }
        }
        public Enums.DirectoryTypeEnum DirectoryType { get; set; } = Enums.DirectoryTypeEnum.UNKNOWN;
        public List<File> RecursiveFiles
        {
            get
            {
                return GetRecursiveFiles(new HashSet<string>());
            }
        }

        private List<File> GetRecursiveFiles(HashSet<string> visitedPaths)
        {
            List<File> ret = new List<File>();

            // Prevent circular references
            string currentPath = UNCPath?.ToLower() ?? Path?.ToLower() ?? "";
            if (visitedPaths.Contains(currentPath))
                return ret;

            visitedPaths.Add(currentPath);

            ret.AddRange(Files);
            foreach (Directory dir in ChildDirectories)
            {
                ret.AddRange(dir.GetRecursiveFiles(visitedPaths));
            }
            return ret;
        }

        public List<Directory> RecursiveChildDirectories
        {
            get
            {
                return GetRecursiveChildDirectories(new HashSet<string>());
            }
        }

        private List<Directory> GetRecursiveChildDirectories(HashSet<string> visitedPaths)
        {
            List<Directory> ret = new List<Directory>();

            // Prevent circular references
            string currentPath = UNCPath?.ToLower() ?? Path?.ToLower() ?? "";
            if (visitedPaths.Contains(currentPath))
                return ret;

            visitedPaths.Add(currentPath);

            ret.AddRange(ChildDirectories);
            foreach (Directory dir in ChildDirectories)
            {
                ret.AddRange(dir.GetRecursiveChildDirectories(visitedPaths));
            }
            return ret;
        }

        public List<File> Files { get; private set; } = new List<File>();

        // ------------------------------------------------------------------
        // Ce que l'énumération n'a pas pu lire. Un sous-dossier fermé par ACL ou
        // une jonction vers son propre parent disparaissaient sans un mot hors -v :
        // l'inventaire se croyait complet. Compté ici, écrit dans le manifeste
        // (`counts.dirs_unreadable`, `unreadable_directories`) et résumé en fin de scan.
        static long _unreadableDirectoryCount;
        static long _reparsePointsSkipped;
        static readonly object _unreadableLock = new();
        static readonly List<string> _unreadableDirectories = new();
        /// <summary>Chemins conservés dans le manifeste (au-delà, seul le compte reste).</summary>
        public const int MAX_UNREADABLE_LISTED = 200;

        /// <summary>Sous-répertoires dont l'énumération a échoué (accès refusé, chemin trop long…).</summary>
        public static long UnreadableDirectoryCount => Interlocked.Read(ref _unreadableDirectoryCount);
        /// <summary>Jonctions et liens symboliques de répertoire ignorés (une boucle ne se termine jamais).</summary>
        public static long ReparsePointsSkipped => Interlocked.Read(ref _reparsePointsSkipped);
        public static IReadOnlyList<string> UnreadableDirectories
        {
            get { lock (_unreadableLock) return new List<string>(_unreadableDirectories); }
        }

        static void RecordUnreadable(string path, Exception ex)
        {
            lock (_unreadableLock)
            {
                if (_unreadableDirectories.Contains(path))
                    return; // déjà compté (fichiers ET sous-dossiers du même répertoire)
                Interlocked.Increment(ref _unreadableDirectoryCount);
                if (_unreadableDirectories.Count < MAX_UNREADABLE_LISTED)
                    _unreadableDirectories.Add(path);
            }
            OutputHelper.WriteError($"répertoire non lu '{path}' : {ex.GetType().Name} {ex.Message}");
        }

        /// <summary>Vide la liste des fichiers déjà écrits en sortie (les sous-répertoires restent à parcourir).</summary>
        public void ClearFiles()
        {
            Files.Clear();
        }
        public List<Directory> ChildDirectories { get; private set; } = new List<Directory>();
        public Directory(string path, Share share)
        {
            Share = share;
            Path = path;
        }
        /// <summary>Un fichier local (Windows ou POSIX) vu par System.IO, enrichi selon les options.</summary>
        private File BuildLocalFile(FileInfo file, ScanOptions opts, string owner)
        {
            // Les horodatages sont lus AVANT toute lecture du contenu : le hash et la
            // signature ouvrent le fichier, ce qui peut mettre à jour la date d'accès.
            DateTime creationTime = file.CreationTime;
            DateTime lastWriteTime = file.LastWriteTime;
            DateTime accessTime = opts.IncludeAccessTime ? file.LastAccessTime : default;
            DateTime? restoreAccessTimeUtc = opts.PreserveAccessTime && opts.ReadsContent ? file.LastAccessTimeUtc : null;
            // `null` et non `0` quand la taille n'est pas collectée : un CSV sans `--sizefile`
            // annonçait sinon un partage entier à 0 octet, que docia excluait « trop petit ».
            long? size = opts.IncludeFileSize ? file.Length : null;
            string attributes = opts.IncludeFileAttributes ? file.Attributes.ToString() : "";
            ContentProbe.Result probe = ContentProbe.ProbeLocal(file.FullName, opts.IncludeFastHash, opts.IncludeFileSignature, opts.Verbose, restoreAccessTimeUtc);
            return new File(
                parentDirectory: this,
                name: file.Name,
                fullName: file.FullName,
                extension: file.Extension,
                creationTime: creationTime,
                lastWriteTime: lastWriteTime,
                fileSize: size,
                accessTime: accessTime,
                fileAttributes: attributes,
                owner: owner,
                fastHash: probe.FastHash,
                fileSignature: probe.FileSignature
            );
        }

        public void FindFilesWindows(List<string> extensionsToIgnore, ScanOptions opts)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(UNCPath).GetFiles("*.*");
                if (opts.Verbose && opts.IncludeAccessTime)
                    OutputHelper.WriteLine($"Collecting access times for {files.Length} files", 2);
                BuildFiles(files, extensionsToIgnore, opts,
                    ownerOf: path => opts.IncludeFileOwner && OperatingSystem.IsWindows() ? WindowsHelper.GetFileOwner(path) : string.Empty);
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"énumération des fichiers impossible dans '{UNCPath}' : {ex.Message}");
            }
        }

        /// <summary>
        /// Construit les <see cref="File"/> d'un répertoire, <c>opts.FileWorkers</c> fichiers à
        /// la fois : propriétaire, empreinte et signature coûtent chacun des allers-retours
        /// vers le serveur de fichiers, et les enchaîner un par un laissait le réseau
        /// attendre (mesuré 4.3.0 : 20 000 fichiers en 19 s, dont 17 s de propriétaires).
        /// L'ordre de sortie reste celui de l'énumération. Un fichier qui disparaît ou se
        /// refuse en cours d'examen est compté (<see cref="UnreadableFileCount"/>) et
        /// sauté — il ne fait plus tomber tout le répertoire.
        /// </summary>
        void BuildFiles(FileInfo[] files, List<string> extensionsToIgnore, ScanOptions opts, Func<string, string> ownerOf)
        {
            File[] built = new File[files.Length];
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opts.FileWorkers) };
            Parallel.For(0, files.Length, parallel, i =>
            {
                FileInfo file = files[i];
                if (extensionsToIgnore?.Contains(file.Extension.ToLower()) == true)
                    return;
                try
                {
                    built[i] = BuildLocalFile(file, opts, ownerOf(file.FullName));
                }
                catch (Exception ex)
                {
                    RecordUnreadableFile(file.FullName, ex, opts.Verbose);
                }
            });
            foreach (File file in built)
            {
                if (file == null)
                    continue;
                if (opts.Verbose)
                    OutputHelper.WriteLine($"[LOCAL-FILE] Processing: {file.Name} (Size: {(file.FileSize?.ToString() ?? "non collectée")}, Owner: {file.Owner})", 3);
                Files.Add(file);
            }
        }

        static long _unreadableFileCount;
        /// <summary>Fichiers sautés parce qu'illisibles en cours d'examen (disparus, refusés).</summary>
        public static long UnreadableFileCount => Interlocked.Read(ref _unreadableFileCount);

        static void RecordUnreadableFile(string path, Exception ex, bool verbose)
        {
            Interlocked.Increment(ref _unreadableFileCount);
            if (verbose)
                OutputHelper.WriteError($"fichier non examiné '{path}' : {ex.GetType().Name} {ex.Message}");
        }

        public void FindFilesCrossPlatform(List<string> extensionsToIgnore, ScanOptions opts)
        {
            try
            {
                NTStatus status;
                ISMBFileStore fileStore = Share.Host.Client.TreeConnect(Share.Name, out status);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    OutputHelper.WriteError($"connexion au partage '{Share.uncPath}' impossible : {status}");
                    return;
                }
                try
                {
                    object directoryHandle;
                    FileStatus fileStatus;
                    status = fileStore.CreateFile(out directoryHandle, out fileStatus, Path, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        OutputHelper.WriteError($"ouverture du répertoire '{UNCPath}' impossible : {status}");
                        return;
                    }
                    try
                    {
                        List<QueryDirectoryFileInformation> fileList;
                        //TODO: can we filter on just files
                        fileStore.QueryDirectory(out fileList, directoryHandle, "*", FileInformationClass.FileDirectoryInformation);
                        if (opts.Verbose && opts.IncludeAccessTime)
                            OutputHelper.WriteLine($"Collecting access times for {fileList.Count} files", 2);
                        foreach (QueryDirectoryFileInformation f in fileList)
                        {
                            if (f.FileInformationClass != FileInformationClass.FileDirectoryInformation)
                                continue;
                            FileDirectoryInformation d = (FileDirectoryInformation)f;
                            if (d.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory))
                                continue;
                            string extension = d.FileName.Substring(d.FileName.LastIndexOf('.') + 1);
                            string path;
                            if (Path == "")
                                path = d.FileName;
                            else
                                path = $"{Path}\\{d.FileName}";
                            if (extensionsToIgnore?.Contains(extension.ToLower()) == true)
                                continue;
                            // Métadonnées issues de QueryDirectory, donc antérieures à toute lecture du contenu
                            DateTime accessTime = opts.IncludeAccessTime ? d.LastAccessTime : default;
                            string owner = opts.IncludeFileOwner ? "<NOT_SUPPORTED>" : string.Empty;
                            DateTime? restoreAccessTime = opts.PreserveAccessTime && opts.ReadsContent ? d.LastAccessTime : null;
                            ContentProbe.Result probe = ContentProbe.ProbeSmb(fileStore, path, opts.IncludeFastHash, opts.IncludeFileSignature, opts.Verbose, restoreAccessTime);
                            Files.Add(
                                new File(
                                    parentDirectory: this,
                                    name: d.FileName,
                                    fullName: path,
                                    extension: extension,
                                    creationTime: d.CreationTime,
                                    lastWriteTime: d.LastWriteTime,
                                    fileSize: opts.IncludeFileSize ? (long)d.EndOfFile : null,
                                    accessTime: accessTime,
                                    fileAttributes: opts.IncludeFileAttributes ? d.FileAttributes.ToString() : "",
                                    owner: owner,
                                    fastHash: probe.FastHash,
                                    fileSignature: probe.FileSignature
                                )
                            );
                        }
                    }
                    finally
                    {
                        fileStore.CloseFile(directoryHandle);
                    }
                }
                finally
                {
                    fileStore.Disconnect();
                }
            }
            catch (Exception ex)
            {
                // Une explosion ne doit pas interrompre toute l'énumération : on journalise et on continue
                OutputHelper.WriteError($"énumération SMB des fichiers impossible dans '{UNCPath}' : {ex.GetType().Name} {ex.Message}");
            }
        }

        public void FindFilesLocal(List<string> extensionsToIgnore, ScanOptions opts)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(UNCPath).GetFiles("*.*");
                if (opts.Verbose)
                {
                    OutputHelper.WriteLine($"[LOCAL-SCAN] Processing directory: {UNCPath} ({files.Length} files)", 2);
                    if (opts.IncludeAccessTime)
                        OutputHelper.WriteLine($"[LOCAL-SCAN] Collecting access times", 2);
                }
                BuildFiles(files, extensionsToIgnore, opts,
                    ownerOf: path => opts.IncludeFileOwner ? LocalHelper.GetFileOwner(path, opts.Verbose) : string.Empty);
            }
            catch (Exception ex)
            {
                RecordUnreadable(UNCPath, ex);
            }
        }
        public void Clear()
        {
            Files.Clear();
            ChildDirectories.Clear();
        }

        private void FindDirectoriesWindows()
        {
            try
            {
                DirectoryInfo[] subDirs = new DirectoryInfo(UNCPath).GetDirectories();
                foreach (DirectoryInfo di in subDirs)
                    ChildDirectories.Add(new Directory(path: di.FullName, share: Share) { Parent = this });
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"énumération des sous-répertoires impossible dans '{UNCPath}' : {ex.Message}");
            }
        }

        private void FindDirectoriesLocal(bool verbose = false)
        {
            try
            {
                DirectoryInfo[] subDirs = new DirectoryInfo(UNCPath).GetDirectories();
                foreach (DirectoryInfo di in subDirs)
                {
                    // Jonction, lien symbolique, point de montage : suivi, un lien vers un
                    // parent fait boucler l'énumération jusqu'au « chemin trop long ». Le
                    // contenu réel est de toute façon scanné par son vrai chemin.
                    if (di.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
                    {
                        Interlocked.Increment(ref _reparsePointsSkipped);
                        if (verbose)
                            OutputHelper.WriteLine($"[LOCAL-SCAN] Reparse point skipped: {di.FullName}", 3);
                        continue;
                    }
                    ChildDirectories.Add(new Directory(path: di.FullName, share: Share) { Parent = this });
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-SCAN] Found subdirectory: {di.FullName}", 3);
                }
            }
            catch (Exception ex)
            {
                RecordUnreadable(UNCPath, ex);
            }
        }
        private void FindDirectoriesCrossPlatform()
        {
            try
            {
                NTStatus status;
                ISMBFileStore fileStore = Share.Host.Client.TreeConnect(Share.Name, out status);
                if (status == NTStatus.STATUS_SUCCESS)
                {
                    try
                    {
                        object directoryHandle;
                        FileStatus fileStatus;
                        status = fileStore.CreateFile(out directoryHandle, out fileStatus, Path, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                        if (status == NTStatus.STATUS_SUCCESS)
                        {
                            try
                            {
                                List<QueryDirectoryFileInformation> fileList;
                                //TODO: can we filter on just files
                                fileStore.QueryDirectory(out fileList, directoryHandle, "*", FileInformationClass.FileDirectoryInformation);
                                foreach (QueryDirectoryFileInformation f in fileList)
                                {
                                    if (f.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                                    {
                                        FileDirectoryInformation d = (FileDirectoryInformation)f;
                                        if (d.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory) && d.FileName != "." && d.FileName != "..")
                                        {
                                            string path = "";
                                            if (Path != "")
                                                path += $"{Path}\\";
                                            path += d.FileName;
                                            ChildDirectories.Add(new Directory(path: path, share: Share) { Parent = this });
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                fileStore.CloseFile(directoryHandle);
                            }
                        }
                    }
                    finally
                    {
                        fileStore.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                // Une explosion ne doit pas interrompre toute l'énumération : on journalise et on continue
                OutputHelper.WriteError($"énumération SMB des sous-répertoires impossible dans '{UNCPath}' : {ex.GetType().Name} {ex.Message}");
            }
        }
        public void FindDirectoriesRecursively(bool crossPlatform, ref bool abort, bool verbose = false)
        {
            bool local = Share != null && Share.Name == "LOCAL_SCAN";
            if (local)
                FindDirectoriesLocal(verbose);
            else if (crossPlatform)
                FindDirectoriesCrossPlatform();
            else
                FindDirectoriesWindows();
            foreach (Directory dir in ChildDirectories)
            {
                if (abort)
                    return;
                dir.FindDirectoriesRecursively(crossPlatform, ref abort, verbose);
            }
        }

        /// <summary>
        /// Énumère les fichiers de ce répertoire puis de ses enfants. Avec
        /// <paramref name="onFilesFound"/>, les fichiers de chaque répertoire sont
        /// remis à l'appelant **aussitôt trouvés** puis oubliés : la mémoire ne
        /// dépend plus du nombre de fichiers du partage mais du plus gros répertoire.
        /// </summary>
        public void FindFilesRecursively(bool crossPlatform, ref bool abort, List<string> extensionsToIgnore, ScanOptions opts, Action<Directory> onFilesFound = null)
        {
            if (opts.Verbose)
            {
                OutputHelper.WriteLine($"Processing directory: {UNCPath}", 3);
            }
            bool local = Share != null && Share.Name == "LOCAL_SCAN";
            if (local)
                FindFilesLocal(extensionsToIgnore, opts);
            else if (crossPlatform)
                FindFilesCrossPlatform(extensionsToIgnore, opts);
            else
                FindFilesWindows(extensionsToIgnore, opts);
            if (onFilesFound != null)
            {
                onFilesFound(this);
                ClearFiles();
            }
            // Iterate only direct children here. Using RecursiveChildDirectories
            // caused repeated traversal of the same subdirectories at every level,
            // dramatically impacting performance when verbose access-time logging
            // was enabled.
            foreach (Directory dir in ChildDirectories)
            {
                if (abort)
                    return;
                dir.FindFilesRecursively(crossPlatform, ref abort, extensionsToIgnore, opts, onFilesFound);
            }
        }

    }
}
