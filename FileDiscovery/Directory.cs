﻿using SMBeagle.ShareDiscovery;
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
        public Directory Base { get
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
        public void FindFilesWindows(List<string> extensionsToIgnore = null, bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false, bool verbose = false)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(UNCPath).GetFiles("*.*");
                if (verbose && includeAccessTime)
                    OutputHelper.WriteLine($"Collecting access times for {files.Length} files", 2);
                foreach (FileInfo file in files)
                {
                    if (extensionsToIgnore.Contains(file.Extension.ToLower()))
                        continue;
                    string owner = string.Empty;
#pragma warning disable CA1416
                    if (includeFileOwner)
                        owner = WindowsHelper.GetFileOwner(file.FullName);
#pragma warning restore CA1416
                    string fastHash = includeFastHash ? WindowsHelper.ComputeFastHash(file.FullName) : string.Empty;
                    string fileSignature = includeFileSignature ? WindowsHelper.DetectFileSignature(file.FullName) : string.Empty;
                    Files.Add(
                        new File(
                            parentDirectory: this,
                            name: file.Name,
                            fullName: file.FullName,
                            extension: file.Extension,
                            creationTime: file.CreationTime,
                            lastWriteTime: file.LastWriteTime,
                            fileSize: includeFileSize ? file.Length : 0,
                            accessTime: includeAccessTime ? file.LastAccessTime : default,
                            fileAttributes: includeFileAttributes ? file.Attributes.ToString() : "",
                            owner: owner,
                            fastHash: fastHash,
                            fileSignature: fileSignature
                        )
                    );
                }
            }
            catch  {            }
        }
        public void FindFilesCrossPlatform(List<string> extensionsToIgnore = null, bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false, bool verbose = false)
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
                                if (verbose && includeAccessTime)
                                    OutputHelper.WriteLine($"Collecting access times for {fileList.Count} files", 2);
                                foreach (QueryDirectoryFileInformation f in fileList)
                                {
                                    if (f.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                                    {
                                        FileDirectoryInformation d = (FileDirectoryInformation)f;
                                        if (! d.FileAttributes.HasFlag(SMBLibrary.FileAttributes.Directory))
                                        {
                                            string extension = d.FileName.Substring(d.FileName.LastIndexOf('.') + 1);
                                            string path;
                                            if (Path == "")
                                                path = d.FileName;
                                            else
                                                path = $"{Path}\\{d.FileName}";
                                            if (extensionsToIgnore?.Contains(extension.ToLower()) == true)
                                                continue;
                                            string owner = includeFileOwner ? "<NOT_SUPPORTED>" : string.Empty;
                                            string fastHash = includeFastHash ? CrossPlatformHelper.ComputeFastHash(fileStore, path) : string.Empty;
                                            string fileSignature = includeFileSignature ? CrossPlatformHelper.DetectFileSignature(fileStore, path) : string.Empty;
                                            Files.Add(
                                                new File(
                                                    parentDirectory: this,
                                                    name: d.FileName,
                                                    fullName: path,
                                                    extension: extension,
                                                    creationTime: d.CreationTime,
                                                    lastWriteTime: d.LastWriteTime,
                                                    fileSize: includeFileSize ? (long)d.EndOfFile : 0,
                                                    accessTime: includeAccessTime ? d.LastAccessTime : default,
                                                    fileAttributes: includeFileAttributes ? d.FileAttributes.ToString() : "",
                                                    owner: owner,
                                                    fastHash: fastHash,
                                                    fileSignature: fileSignature
                                                )
                                            );
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
            catch 
            {
                //TODO: Implement better error handling here, one explosion should not wipe out the whole enumeration
            }
        }

        public void FindFilesLocal(List<string> extensionsToIgnore = null, bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false, bool verbose = false)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(UNCPath).GetFiles("*.*");
                if (verbose)
                {
                    OutputHelper.WriteLine($"[LOCAL-SCAN] Processing directory: {UNCPath} ({files.Length} files)",2);
                    if (includeAccessTime)
                        OutputHelper.WriteLine($"[LOCAL-SCAN] Collecting access times",2);
                }
                foreach (FileInfo file in files)
                {
                    if (extensionsToIgnore?.Contains(file.Extension.ToLower()) == true)
                        continue;
                    string owner = string.Empty;
                    if (includeFileOwner)
                        owner = LocalHelper.GetFileOwner(file.FullName, verbose);
                    string fastHash = includeFastHash ? LocalHelper.ComputeFastHash(file.FullName, verbose) : string.Empty;
                    string fileSignature = includeFileSignature ? LocalHelper.DetectFileSignature(file.FullName, verbose) : string.Empty;
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-FILE] Processing: {file.Name} (Size: {file.Length}, Owner: {owner})",3);
                    Files.Add(
                        new File(
                            parentDirectory: this,
                            name: file.Name,
                            fullName: file.FullName,
                            extension: file.Extension,
                            creationTime: file.CreationTime,
                            lastWriteTime: file.LastWriteTime,
                            fileSize: includeFileSize ? file.Length : 0,
                            accessTime: includeAccessTime ? file.LastAccessTime : default,
                            fileAttributes: includeFileAttributes ? file.Attributes.ToString() : "",
                            owner: owner,
                            fastHash: fastHash,
                            fileSignature: fileSignature
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteLine($"[LOCAL-SCAN] Error enumerating files in {UNCPath}: {ex.Message}",2);
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
                    ChildDirectories.Add(new Directory(path: di.FullName, share: Share) { Parent = this});
            }
            catch { }
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
                        OutputHelper.WriteLine($"[LOCAL-SCAN] Found subdirectory: {di.FullName}",3);
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteLine($"[LOCAL-SCAN] Error enumerating directories in {UNCPath}: {ex.Message}",2);
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
                                        FileDirectoryInformation d = (FileDirectoryInformation) f;
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
            catch 
            {
                //TODO: Implement better error handling here, one explosion should not wipe out the whole enumeration
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

        public void FindFilesRecursively(bool crossPlatform, ref bool abort, List<string> extensionsToIgnore = null, bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false, bool verbose = false)
        {
            if (verbose)
            {
                OutputHelper.WriteLine($"Processing directory: {UNCPath}", 3);
            }
            bool local = Share != null && Share.Name == "LOCAL_SCAN";
            if (local)
                FindFilesLocal(extensionsToIgnore, includeFileSize, includeAccessTime, includeFileAttributes, includeFileOwner, includeFastHash, includeFileSignature, verbose);
            else if (crossPlatform)
                FindFilesCrossPlatform(extensionsToIgnore, includeFileSize, includeAccessTime, includeFileAttributes, includeFileOwner, includeFastHash, includeFileSignature, verbose);
            else
                FindFilesWindows(extensionsToIgnore, includeFileSize, includeAccessTime, includeFileAttributes, includeFileOwner, includeFastHash, includeFileSignature, verbose);
            // Iterate only direct children here. Using RecursiveChildDirectories
            // caused repeated traversal of the same subdirectories at every level,
            // dramatically impacting performance when verbose access-time logging
            // was enabled.
            foreach (Directory dir in ChildDirectories)
            {
                if (abort)
                    return;
                dir.FindFilesRecursively(crossPlatform, ref abort, extensionsToIgnore, includeFileSize, includeAccessTime, includeFileAttributes, includeFileOwner, includeFastHash, includeFileSignature, verbose);
            }
        }

    }
}
