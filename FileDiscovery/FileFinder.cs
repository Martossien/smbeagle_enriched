using SMBeagle.HostDiscovery;
using SMBeagle.Output;
using SMBeagle.ShareDiscovery;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SMBeagle.FileDiscovery
{
    class FileFinder
    {
        Dictionary<string, ACL> CacheACL { get; set; } = new();
        IntPtr pClientContext { get; set; }
        HashSet<string> FilesSentForOutput { get; set; } = new();

        bool _localScan = false;

        List<Directory> _directories { get; set; } = new();

        readonly List<string> _rootPaths = new();
        /// <summary>
        /// Cibles réellement scannées, telles que demandées. Lue AVANT
        /// <see cref="SplitLargeDirectories"/> : celui-ci remplace une racine de plus de
        /// 20 sous-répertoires par ses enfants, et « les répertoires sans parent »
        /// devenaient une liste vide — le scan sortait en code 3 « aucun fichier » avec
        /// 100 000 lignes écrites et un manifeste sans cible.
        /// </summary>
        public IReadOnlyList<string> RootPaths => _rootPaths;
        public List<Directory> Directories
        {
            get
            {
                List<Directory>
                    ret = new();

                ret.AddRange(_directories);

                foreach (Directory dir in _directories)
                {
                    ret.AddRange(dir.RecursiveChildDirectories);
                }

                return ret;
            }
        }

        public List<File> Files
        {
            get
            {
                HashSet<string> seenFiles = new HashSet<string>();
                List<File> uniqueFiles = new List<File>();

                foreach (Directory dir in Directories)
                {
                    foreach (File file in dir.RecursiveFiles)
                    {
                        string fileKey = $"{dir.Share.uncPath}{file.FullName}".ToLower();
                        if (seenFiles.Add(fileKey))
                        {
                            uniqueFiles.Add(file);
                        }
                    }
                }

                return uniqueFiles;
            }
        }

        readonly ScanOptions _opts;

        public FileFinder(ScanOptions opts)
        {
            _opts = opts;
            if (opts.FetchFiles)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(opts.OutputDirectory);
                }
                catch (Exception ex)
                {
                    OutputHelper.WriteError($"création du répertoire de butin '{opts.OutputDirectory}' impossible : {ex.Message}");
                    throw;
                }
            }
            pClientContext = IntPtr.Zero;
            if (!opts.CrossPlatform && OperatingSystem.IsWindows())
            {
                pClientContext = WindowsHelper.GetpClientContext();
                if (opts.EnumerateAcls & pClientContext == IntPtr.Zero & !opts.Quiet)
                {
                    OutputHelper.WriteLine("!! Error querying user context.  Failing back to a slower ACL identifier.  ", 1);
                    OutputHelper.WriteLine("    We can also no longer check  if a file is deletable", 1);
                    if (!opts.GetPermissionsForSingleFileInDir)
                        OutputHelper.WriteLine("    It is advisable to set the fast flag and only check the ACLs of one file per directory", 1);
                }
            }

            if (opts.IsLocalScan)
            {
                _localScan = true;
                _directories.AddRange(GetLocalPathDirectories(opts.LocalPaths, opts.Verbose));
            }
            else
            {
                foreach (Share share in opts.Shares) //TODO: dedup share by host and name
                {
                    _directories.Add(new Directory(path: "", share: share) { DirectoryType = Enums.DirectoryTypeEnum.SMB });
                }
            }
            _rootPaths.AddRange(_directories.Select(d => _localScan ? d.Path : d.UNCPath));

            if (!opts.Quiet)
                OutputHelper.WriteLine($"6a. Enumerating all subdirectories for known paths");

            bool abort = false;

#nullable enable
            System.ConsoleCancelEventHandler handler = (object? sender, ConsoleCancelEventArgs e) =>
            {
#nullable disable
                if (e.SpecialKey.HasFlag(ConsoleSpecialKey.ControlBreak))
                {
                    e.Cancel = true;
                    abort = true;
                    OutputHelper.WriteLine("\nSKIPPING");
                }
                else
                {
                    OutputHelper.WriteLine("\nABORTED EXECUTION... Did you mean CTRL-BREAK?");
                    ProgressReporter.Current?.Error("interrompu par l'utilisateur (CTRL-C)");
                    Environment.Exit(ExitCodes.RuntimeError);
                }
            };

            Console.CancelKeyPress += handler;

            bool crossPlatform = _localScan ? false : opts.CrossPlatform;
            foreach (Directory dir in _directories)
            {
                if (!opts.Quiet)
                    OutputHelper.WriteLine($"\rEnumerating all subdirectories for '{dir.UNCPath}' - CTRL-BREAK or CTRL-PAUSE to SKIP                                 ", 1, false);
                dir.FindDirectoriesRecursively(crossPlatform: crossPlatform, ref abort, opts.Verbose);
                abort = false;
            }

            Console.CancelKeyPress -= handler;

            if (!opts.Quiet)
                OutputHelper.WriteLine($"\r6b. Splitting large directories to optimise caching and to batch output                                              ");

            SplitLargeDirectories();

            if (!opts.Quiet)
                OutputHelper.WriteLine($"6c. Enumerating files in directories");

            Console.CancelKeyPress += handler;
            var tasks = new List<Task>();
            var extensionsToIgnore = new List<string>() { ".dll", ".manifest", ".cat" };
            foreach (Directory dir in _directories)
            {
                abort = false;
                if (!opts.Quiet)
                    OutputHelper.WriteLine($"\renumerating files in '{dir.UNCPath}' - CTRL-BREAK or CTRL-PAUSE to SKIP                                          ", 1, false);
                long before = FilesSentForOutput.Count;
                // Les fichiers de chaque répertoire sont écrits dès leur énumération, puis
                // oubliés : garder l'arbre entier en mémoire jusqu'à la fin coûtait ~170 Mo
                // pour 100 000 fichiers, et autant de plus par tranche de 100 000.
                dir.FindFilesRecursively(crossPlatform: crossPlatform, ref abort, extensionsToIgnore: extensionsToIgnore, opts: opts,
                    onFilesFound: found => EmitFiles(found, crossPlatform, tasks));
                if (opts.Verbose)
                    OutputHelper.WriteLine($"\rFound {dir.ChildDirectories.Count} child directories and {FilesSentForOutput.Count - before} files in '{dir.UNCPath}'", 2);

                dir.Clear();
                CacheACL.Clear(); // Clear Cached ACLs otherwise it grows and grows
            }
            Task.WaitAll(tasks.ToArray());
            Console.CancelKeyPress -= handler;
            if (!opts.Quiet)
                OutputHelper.WriteLine($"\r  file enumeration complete, {FilesSentForOutput.Count} files identified                ");
            if (Directory.UnreadableDirectoryCount > 0)
                OutputHelper.WriteError($"{Directory.UnreadableDirectoryCount} répertoire(s) non lu(s) (accès refusé ou chemin illisible) : leurs fichiers sont absents de l'inventaire — voir le manifeste");
            if (Directory.ReparsePointsSkipped > 0 && !opts.Quiet)
                OutputHelper.WriteLine($"  {Directory.ReparsePointsSkipped} jonction(s)/lien(s) de répertoire ignoré(s) (contenu scanné par son vrai chemin)", 1);
            if (opts.PreserveAccessTime && ContentProbe.AccessTimeRestoreFailures > 0)
                OutputHelper.WriteError($"date d'accès non restaurée pour {ContentProbe.AccessTimeRestoreFailures} fichier(s) (droits insuffisants ; -v pour le détail)");
        }

        /// <summary>Nombre de fichiers envoyés en sortie (dédoublonnés).</summary>
        public int FileCount => FilesSentForOutput.Count;

        /// <summary>Permissions, sortie, progression et récupération des fichiers d'UN répertoire.</summary>
        void EmitFiles(Directory dir, bool crossPlatform, List<Task> tasks)
        {
            ScanOptions opts = _opts;
            foreach (File file in dir.Files)
            {
                string fileKey = $"{dir.Share.uncPath}{file.FullName}".ToLower();
                bool addedToSet;
                lock (FilesSentForOutput)
                {
                    addedToSet = FilesSentForOutput.Add(fileKey);
                }
                if (!addedToSet) // déjà écrit (même fichier vu par deux racines)
                    continue;
                if (opts.EnumerateAcls)
                {
                    if (_localScan)
                        FetchFilePermissionLocal(file);
                    else
                        FetchFilePermission(file, crossPlatform, opts.GetPermissionsForSingleFileInDir);
                }
                OutputHelper.AddPayload(new Output.FileOutput(file), Enums.OutputtersEnum.File);
                ProgressReporter.Current?.Files(FilesSentForOutput.Count);
                if (opts.FetchFiles && opts.FilePatterns.Any(pattern => Regex.IsMatch(file.Name, pattern, RegexOptions.IgnoreCase)))
                {
                    if (_localScan)
                        tasks.Add(Task.Run(() => FetchFileLocal(file, opts.OutputDirectory)));
                    else
                        tasks.Add(Task.Run(() => FetchFile(file, crossPlatform, opts.OutputDirectory)));
                    if (crossPlatform)
                        Task.WaitAll(tasks.ToArray());
                }
            }
        }

        private Enums.DirectoryTypeEnum DriveInfoTypeToDirectoryTypeEnum(DriveType type)
        {
            return type switch
            {
                DriveType.Fixed => Enums.DirectoryTypeEnum.LOCAL_FIXED,
                DriveType.CDRom => Enums.DirectoryTypeEnum.LOCAL_CDROM,
                DriveType.Network => Enums.DirectoryTypeEnum.LOCAL_NETWORK,
                DriveType.Removable => Enums.DirectoryTypeEnum.LOCAL_REMOVEABLE,
                _ => Enums.DirectoryTypeEnum.UNKNOWN
            };
        }

        private List<Directory> GetLocalPathDirectories(List<string> localPaths, bool verbose = false)
        {
            var directories = new List<Directory>();
            var validatedPaths = new List<string>();
            var dummyHost = new HostDiscovery.Host("localhost");
            var dummyShare = new ShareDiscovery.Share(dummyHost, "LOCAL_SCAN", Enums.ShareTypeEnum.DISK);

            foreach (string path in localPaths)
            {
                try
                {
                    string fullPath = System.IO.Path.GetFullPath(path);
                    if (!System.IO.Directory.Exists(fullPath))
                    {
                        OutputHelper.WriteLine($"ERROR: Directory not found: {fullPath}", 1);
                        continue;
                    }
                    try { System.IO.Directory.GetDirectories(fullPath); }
                    catch (UnauthorizedAccessException)
                    {
                        OutputHelper.WriteLine($"ERROR: Access denied to directory: {fullPath}", 1);
                        continue;
                    }
                    validatedPaths.Add(fullPath);
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-VALIDATION] Valid path added: {fullPath}", 2);
                }
                catch (Exception ex)
                {
                    OutputHelper.WriteLine($"ERROR: Cannot process path '{path}': {ex.Message}", 1);
                }
            }

            if (validatedPaths.Count == 0)
            {
                OutputHelper.WriteLine("ERROR: No valid local paths found. Exiting local scan.", 1);
                return directories;
            }

            foreach (string validPath in validatedPaths)
            {
                directories.Add(new Directory(path: validPath, share: dummyShare)
                {
                    DirectoryType = Enums.DirectoryTypeEnum.LOCAL_FIXED
                });
            }

            if (verbose)
                OutputHelper.WriteLine($"[LOCAL-VALIDATION] Created {directories.Count} directory objects for scanning", 1);

            return directories;
        }

        //TODO: Reimplement at some point
        /*private List<Directory> GetLocalDriveDirectories()
        {
            // Create dummy sahre
            Share dummyShare = new Share(new HostDiscovery.Host("localhost"), "", Enums.ShareTypeEnum.DISK);
            return DriveInfo
                .GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new Directory(drive.Name, share: dummyShare) { DirectoryType = DriveInfoTypeToDirectoryTypeEnum(drive.DriveType) })
                .ToList();
        }*/

        private void SplitLargeDirectories(int maxChildCount = 20)
        {
            HashSet<string> processedPaths = new HashSet<string>();
            bool hasChanges = true;

            while (hasChanges)
            {
                hasChanges = false;
                List<Directory> currentDirectories = new List<Directory>(_directories);

                foreach (Directory dir in currentDirectories)
                {
                    if (dir.RecursiveChildDirectories.Count > maxChildCount)
                    {
                        _directories.Remove(dir);

                        foreach (Directory childDir in dir.ChildDirectories)
                        {
                            string childPath = childDir.UNCPath.ToLower();
                            if (processedPaths.Add(childPath))
                            {
                                _directories.Add(childDir);
                                hasChanges = true;
                            }
                        }
                    }
                }
            }
        }

        private void FetchFilePermission(File file, bool crossPlatform, bool useCache = true)
        {
            if (useCache && CacheACL.Keys.Contains(file.ParentDirectory.Path)) // If we should use cache and cache has a hit
                file.SetPermissionsFromACL(CacheACL[file.ParentDirectory.Path]);
            else
            {
                ACL permissions;
                if (!crossPlatform)
#pragma warning disable CA1416
                {
                    if (pClientContext != IntPtr.Zero)
                        permissions = WindowsHelper.ResolvePermissions(file.FullName, pClientContext);

                    else
                        permissions = WindowsHelper.ResolvePermissionsSlow(file.FullName);

                }
#pragma warning restore CA1416
                else
                {
                    permissions = CrossPlatformHelper.ResolvePermissions(file);
                }
                file.SetPermissionsFromACL(permissions);

                if (useCache)
                    CacheACL[file.ParentDirectory.Path] = permissions;
            }
        }

        private void FetchFilePermissionLocal(File file)
        {
            ACL permissions = LocalHelper.ResolvePermissions(file.FullName);
            file.SetPermissionsFromACL(permissions);
        }

        private void FetchFileLocal(File file, string outputDirectory)
        {
            try
            {
                string filename = $"{outputDirectory}{Path.DirectorySeparatorChar}{file.FullName}".Replace("\\", "_").Replace("/", "_");
                System.IO.File.Copy(file.FullName, filename, true);
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"copie de '{file.FullName}' impossible : {ex.Message}");
            }
        }

        private void FetchFile(File file, bool crossPlatform, string outputDirectory)
        {
            // Même nom de sortie sur les deux chemins (le chemin Windows préfixait deux fois le répertoire de butin)
            string outputFilename = $"{file.ParentDirectory.Share.uncPath}{file.FullName}".Replace("\\", "_").Replace("/", "_");
            outputFilename = $"{outputDirectory}{Path.DirectorySeparatorChar}{outputFilename}";
            try
            {
                if (!crossPlatform && OperatingSystem.IsWindows())
                    WindowsHelper.RetrieveFile(file, outputFilename);
                else
                    CrossPlatformHelper.RetrieveFile(file, outputFilename);
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"récupération de '{file.FullName}' impossible : {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}
