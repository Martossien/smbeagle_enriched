using SMBeagle.ShareDiscovery;
using SMBeagle.Output;
using SMBLibrary;
using SMBLibrary.Client;
using System;
using System.Collections.Generic;
using System.IO;

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
            long size = opts.IncludeFileSize ? file.Length : 0;
            string attributes = opts.IncludeFileAttributes ? file.Attributes.ToString() : "";
            ContentProbe.Result probe = ContentProbe.ProbeLocal(file.FullName, opts.IncludeFastHash, opts.IncludeFileSignature, opts.Verbose);
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
                foreach (FileInfo file in files)
                {
                    if (extensionsToIgnore?.Contains(file.Extension.ToLower()) == true)
                        continue;
                    string owner = string.Empty;
                    if (opts.IncludeFileOwner && OperatingSystem.IsWindows())
                        owner = WindowsHelper.GetFileOwner(file.FullName);
                    Files.Add(BuildLocalFile(file, opts, owner));
                }
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"énumération des fichiers impossible dans '{UNCPath}' : {ex.Message}");
            }
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
                            ContentProbe.Result probe = ContentProbe.ProbeSmb(fileStore, path, opts.IncludeFastHash, opts.IncludeFileSignature, opts.Verbose);
                            Files.Add(
                                new File(
                                    parentDirectory: this,
                                    name: d.FileName,
                                    fullName: path,
                                    extension: extension,
                                    creationTime: d.CreationTime,
                                    lastWriteTime: d.LastWriteTime,
                                    fileSize: opts.IncludeFileSize ? (long)d.EndOfFile : 0,
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
                foreach (FileInfo file in files)
                {
                    if (extensionsToIgnore?.Contains(file.Extension.ToLower()) == true)
                        continue;
                    string owner = string.Empty;
                    if (opts.IncludeFileOwner)
                        owner = LocalHelper.GetFileOwner(file.FullName, opts.Verbose);
                    File built = BuildLocalFile(file, opts, owner);
                    if (opts.Verbose)
                        OutputHelper.WriteLine($"[LOCAL-FILE] Processing: {file.Name} (Size: {built.FileSize}, Owner: {owner})", 3);
                    Files.Add(built);
                }
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"énumération des fichiers impossible dans '{UNCPath}' : {ex.Message}");
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
                    ChildDirectories.Add(new Directory(path: di.FullName, share: Share) { Parent = this });
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-SCAN] Found subdirectory: {di.FullName}", 3);
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteLine($"[LOCAL-SCAN] Error enumerating directories in {UNCPath}: {ex.Message}", 2);
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

        public void FindFilesRecursively(bool crossPlatform, ref bool abort, List<string> extensionsToIgnore, ScanOptions opts)
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
            // Iterate only direct children here. Using RecursiveChildDirectories
            // caused repeated traversal of the same subdirectories at every level,
            // dramatically impacting performance when verbose access-time logging
            // was enabled.
            foreach (Directory dir in ChildDirectories)
            {
                if (abort)
                    return;
                dir.FindFilesRecursively(crossPlatform, ref abort, extensionsToIgnore, opts);
            }
        }

    }
}
