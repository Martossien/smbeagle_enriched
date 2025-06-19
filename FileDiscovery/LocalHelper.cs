using System;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Mono.Unix;

namespace SMBeagle.FileDiscovery
{
    static class LocalHelper
    {
        public static ACL ResolvePermissions(string path, bool verbose = false)
        {
            ACL acl = new();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { new FileStream(path, FileMode.Open, FileAccess.Read).Dispose(); acl.Readable = true; } catch { }
                try { new FileStream(path, FileMode.Open, FileAccess.Write).Dispose(); acl.Writeable = true; } catch { }
                return acl;
            }
            else
            {
                try
                {
                    var fileInfo = new UnixFileInfo(path);
                    FileAccessPermissions perms = fileInfo.FileAccessPermissions;
                    acl.Readable = (perms & (FileAccessPermissions.UserRead | FileAccessPermissions.GroupRead | FileAccessPermissions.OtherRead)) != 0;
                    acl.Writeable = (perms & (FileAccessPermissions.UserWrite | FileAccessPermissions.GroupWrite | FileAccessPermissions.OtherWrite)) != 0;
                    var dirInfo = new UnixDirectoryInfo(Path.GetDirectoryName(path));
                    acl.Deletable = (dirInfo.FileAccessPermissions & FileAccessPermissions.UserWrite) != 0;
                    if (verbose)
                        Output.OutputHelper.WriteLine($"[LOCAL-ACL] Linux permissions R:{acl.Readable}/W:{acl.Writeable}/D:{acl.Deletable} for {Path.GetFileName(path)}",3);
                }
                catch (Exception ex)
                {
                    if (verbose)
                        Output.OutputHelper.WriteLine($"[LOCAL-ACL] Error getting Linux permissions for {Path.GetFileName(path)}: {ex.Message}",3);
                    acl.Readable = File.Exists(path);
                }
                return acl;
            }
        }

        public static string ComputeFastHash(string filePath, bool verbose = false)
        {
            const int READ_SIZE = 65536;
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int toRead = (int)Math.Min(READ_SIZE, fs.Length);
                byte[] buffer = new byte[toRead];
                int read = fs.Read(buffer, 0, toRead);
                ulong hash = XxHash64.HashToUInt64(buffer.AsSpan(0, read));
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-HASH] Computed hash for: {Path.GetFileName(filePath)}",3);
                return hash.ToString("x16");
            }
            catch (UnauthorizedAccessException)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-HASH] Access denied: {Path.GetFileName(filePath)}",3);
                return "<ACCESS_DENIED>";
            }
            catch (FileNotFoundException)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-HASH] File not found: {Path.GetFileName(filePath)}",3);
                return "<FILE_NOT_FOUND>";
            }
            catch (IOException ex)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-HASH] I/O error for {Path.GetFileName(filePath)}: {ex.Message}",3);
                return "<IO_ERROR>";
            }
            catch (Exception ex)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-HASH] Unexpected error for {Path.GetFileName(filePath)}: {ex.GetType().Name}",3);
                return $"<ERROR_{ex.GetType().Name}>";
            }
        }

        public static string DetectFileSignature(string filePath, bool verbose = false)
        {
            const int READ_SIZE = 32;
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int toRead = (int)Math.Min(READ_SIZE, fs.Length);
                byte[] buffer = new byte[toRead];
                int read = fs.Read(buffer, 0, toRead);
                using MemoryStream ms = new MemoryStream(buffer, 0, read);
                var inspector = new FileSignatures.FileFormatInspector();
                var format = inspector.DetermineFileFormat(ms);
                string result = format == null ? "unknown" : format.Extension.TrimStart('.').ToLower();
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-SIGN] Signature for {Path.GetFileName(filePath)}: {result}",3);
                return result;
            }
            catch (UnauthorizedAccessException)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-SIGN] Access denied: {Path.GetFileName(filePath)}",3);
                return "<ACCESS_DENIED>";
            }
            catch (FileNotFoundException)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-SIGN] File not found: {Path.GetFileName(filePath)}",3);
                return "<FILE_NOT_FOUND>";
            }
            catch (IOException ex)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-SIGN] I/O error for {Path.GetFileName(filePath)}: {ex.Message}",3);
                return "<IO_ERROR>";
            }
            catch (Exception ex)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-SIGN] Unexpected error for {Path.GetFileName(filePath)}: {ex.GetType().Name}",3);
                return $"<ERROR_{ex.GetType().Name}>";
            }
        }

        public static string GetFileOwner(string filePath, bool verbose = false)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#pragma warning disable CA1416
                return WindowsHelper.GetFileOwner(filePath);
#pragma warning restore CA1416
            }
            try
            {
                var fileInfo = new UnixFileInfo(filePath);
                var ownerInfo = fileInfo.OwnerUser;
                var groupInfo = fileInfo.OwnerGroup;
                string result = $"{ownerInfo.UserName}:{groupInfo.GroupName}";
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-OWNER] Linux owner: {result} for {Path.GetFileName(filePath)}",3);
                return result;
            }
            catch (Exception ex)
            {
                if (verbose)
                    Output.OutputHelper.WriteLine($"[LOCAL-OWNER] Error getting Linux owner for {Path.GetFileName(filePath)}: {ex.Message}",3);
                return $"<LINUX_ERROR_{ex.GetType().Name}>";
            }
        }
    }
}
