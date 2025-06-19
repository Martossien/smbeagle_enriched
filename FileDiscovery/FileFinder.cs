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
        public List<Directory> Directories
        {
            get
            {
                List<Directory> 
                    ret = new ();

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

        bool _includeFileSize;
        bool _includeAccessTime;
        bool _includeFileAttributes;
        bool _includeFileOwner;
        bool _includeFastHash;
        bool _includeFileSignature;
        public FileFinder(List<Share> shares, string outputDirectory, bool fetchFiles, List<String> filePatterns, bool getPermissionsForSingleFileInDir = true, bool enumerateAcls = true, bool quiet = false, bool verbose = false, bool crossPlatform = false, bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false, List<string>? localPaths = null)
        {
            _includeFileSize = includeFileSize;
            _includeAccessTime = includeAccessTime;
            _includeFileAttributes = includeFileAttributes;
            _includeFileOwner = includeFileOwner;
            _includeFastHash = includeFastHash;
            _includeFileSignature = includeFileSignature;
            if (fetchFiles)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(outputDirectory);
                }
                catch
                {
                    Console.WriteLine($"\nERROR: CANNOT CREATE LOOT DIR {outputDirectory}");
                    Environment.Exit(0);
                }
            }
            pClientContext = IntPtr.Zero;
            if (! crossPlatform)
            {
                #pragma warning disable CA1416
                pClientContext = WindowsHelper.GetpClientContext();
                if (enumerateAcls & pClientContext == IntPtr.Zero & !quiet)
                {
                    OutputHelper.WriteLine("!! Error querying user context.  Failing back to a slower ACL identifier.  ", 1);
                    OutputHelper.WriteLine("    We can also no longer check  if a file is deletable", 1);
                    if (!getPermissionsForSingleFileInDir)
                        OutputHelper.WriteLine("    It is advisable to set the fast flag and only check the ACLs of one file per directory", 1);
                }
                #pragma warning restore CA1416
            }

            if (localPaths != null && localPaths.Any())
            {
                _localScan = true;
                _directories.AddRange(GetLocalPathDirectories(localPaths, verbose));
            }
            else
            {
                foreach (Share share in shares) //TODO: dedup share by host and name
                {
                    _directories.Add(new Directory(path: "", share: share) { DirectoryType = Enums.DirectoryTypeEnum.SMB });
                }
            }

            if (!quiet)
                OutputHelper.WriteLine($"6a. Enumerating all subdirectories for known paths");

            bool abort = false;

            #nullable enable
            System.ConsoleCancelEventHandler handler = (object? sender, ConsoleCancelEventArgs e) => {
            #nullable disable
                if (e.SpecialKey.HasFlag(ConsoleSpecialKey.ControlBreak))
                {
                    e.Cancel = true;
                    abort = true;
                    Console.WriteLine("\nSKIPPING");
                }
                else
                {
                    Console.WriteLine("\nABORTED EXECUTION... Did you mean CTRL-BREAK?");
                    Environment.Exit(0);
                }
            };

            Console.CancelKeyPress += handler;

            foreach (Directory dir in _directories)
            {
                OutputHelper.WriteLine($"\rEnumerating all subdirectories for '{dir.UNCPath}' - CTRL-BREAK or CTRL-PAUSE to SKIP                                 ", 1, false);
                bool useCross = _localScan ? false : crossPlatform;
                dir.FindDirectoriesRecursively(crossPlatform: useCross, ref abort);
                abort = false;
            }

            Console.CancelKeyPress -= handler;

            if (!quiet)
                OutputHelper.WriteLine($"\r6b. Splitting large directories to optimise caching and to batch output                                              ");

            SplitLargeDirectories();

            if (!quiet)
                OutputHelper.WriteLine($"6c. Enumerating files in directories");

            Console.CancelKeyPress += handler;
            var tasks = new List<Task>();
            foreach (Directory dir in _directories)
            {
                abort = false;
                OutputHelper.WriteLine($"\renumerating files in '{dir.UNCPath}' - CTRL-BREAK or CTRL-PAUSE to SKIP                                          ", 1, false);
                var extensionsToIgnore = new List<string>() { ".dll", ".manifest", ".cat" };
                bool useCrossFiles = _localScan ? false : crossPlatform;
                dir.FindFilesRecursively(crossPlatform: useCrossFiles, ref abort, extensionsToIgnore: extensionsToIgnore, includeFileSize: _includeFileSize, includeAccessTime: _includeAccessTime, includeFileAttributes: _includeFileAttributes, includeFileOwner: _includeFileOwner, includeFastHash: _includeFastHash, includeFileSignature: _includeFileSignature, verbose: verbose);
                if (verbose)
                    OutputHelper.WriteLine($"\rFound {dir.ChildDirectories.Count} child directories and {dir.RecursiveFiles.Count} files in '{dir.UNCPath}'",2);
                
                var filesToProcess = new List<File>(dir.RecursiveFiles);
                foreach (File file in filesToProcess)
                {
                    string fileKey = $"{dir.Share.uncPath}{file.FullName}".ToLower();
                    bool addedToSet;
                    lock (FilesSentForOutput)
                    {
                        addedToSet = FilesSentForOutput.Add(fileKey);
                    }
                    
                    if (addedToSet) // returns True if not already present
                    {
                        if (enumerateAcls)
                        {
                            if (_localScan)
                                FetchFilePermissionLocal(file);
                            else
                                FetchFilePermission(file, crossPlatform, getPermissionsForSingleFileInDir);
                        }

						OutputHelper.AddPayload(new Output.FileOutput(file), Enums.OutputtersEnum.File);

						if (fetchFiles && filePatterns?.Any(pattern => Regex.IsMatch(file.Name, pattern, RegexOptions.IgnoreCase)) == true)
                        {
                            if (_localScan)
                                tasks.Add(Task.Run(() => FetchFileLocal(file, outputDirectory)));
                            else
                                tasks.Add(Task.Run(() => FetchFile(file, crossPlatform, outputDirectory)));
                            if (crossPlatform && !_localScan)
							    Task.WaitAll(tasks.ToArray());
						}
					}
                }

                dir.Clear();
                CacheACL.Clear(); // Clear Cached ACLs otherwise it grows and grows
            }
			Task.WaitAll(tasks.ToArray());
			Console.CancelKeyPress -= handler;
            OutputHelper.WriteLine($"\r  file enumeration complete, {FilesSentForOutput.Count} files identified                ");
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
            var dummyHost = new HostDiscovery.Host("localhost");
            var dummyShare = new ShareDiscovery.Share(dummyHost, "LOCAL_SCAN", Enums.ShareTypeEnum.DISK);

            foreach (string path in localPaths)
            {
                try
                {
                    if (!System.IO.Directory.Exists(path))
                    {
                        OutputHelper.WriteLine($"ERROR: Directory not found: {path}", 1);
                        continue;
                    }
                    directories.Add(new Directory(path: path, share: dummyShare)
                    {
                        DirectoryType = Enums.DirectoryTypeEnum.LOCAL_FIXED
                    });
                    if (verbose)
                        OutputHelper.WriteLine($"Added local directory for scanning: {path}", 1);
                }
                catch (Exception ex)
                {
                    OutputHelper.WriteLine($"ERROR: Cannot process path {path}: {ex.Message}", 1);
                }
            }
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
            catch
            {
                // ignore errors during copy
            }
        }

		private void FetchFile(File file, bool crossPlatform, string outputDirectory)
		{
            byte[] fileBytes;
            string filename;
			if (!crossPlatform)
#pragma warning disable CA1416
			{
                // TODO: Add windows method
				filename = $"{outputDirectory}{Path.DirectorySeparatorChar}{file.FullName}".Replace("\\", "_").Replace("/", "_");
				filename = $"{outputDirectory}{Path.DirectorySeparatorChar}{filename}";
				WindowsHelper.RetrieveFile(file, filename);
			}
#pragma warning restore CA1416
			else
			{
				filename = $"{file.ParentDirectory.Share.uncPath}{file.FullName}".Replace("\\", "_").Replace("/", "_");
                filename = $"{outputDirectory}{Path.DirectorySeparatorChar}{filename}";
                CrossPlatformHelper.RetrieveFile(file, filename);
			}

		}
	}
}
