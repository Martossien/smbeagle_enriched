using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Unix;
using SMBeagle.Output;

namespace SMBeagle.FileDiscovery
{
    static class LocalHelper
    {
        public static ACL ResolvePermissions(string path, bool verbose = false)
        {
            ACL acl = new();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { new FileStream(path, FileMode.Open, FileAccess.Read).Dispose(); acl.Readable = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-ACL] Not readable: {Path.GetFileName(path)} ({ex.GetType().Name})", 3);
                }
                try { new FileStream(path, FileMode.Open, FileAccess.Write).Dispose(); acl.Writeable = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-ACL] Not writeable: {Path.GetFileName(path)} ({ex.GetType().Name})", 3);
                }
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
                        OutputHelper.WriteLine($"[LOCAL-ACL] Linux permissions R:{acl.Readable}/W:{acl.Writeable}/D:{acl.Deletable} for {Path.GetFileName(path)}", 3);
                }
                catch (Exception ex)
                {
                    if (verbose)
                        OutputHelper.WriteLine($"[LOCAL-ACL] Error getting Linux permissions for {Path.GetFileName(path)}: {ex.Message}", 3);
                    acl.Readable = System.IO.File.Exists(path);
                }
                return acl;
            }
        }

        // Résolution uid/gid → nom mise en cache : `getpwuid`/`getgrgid` interrogent la
        // base des comptes (NSS, donc parfois un annuaire) à CHAQUE appel — mesuré
        // 0,85 ms par fichier, 17 s sur 20 000 fichiers pour une poignée de comptes
        // distincts. Verrou : ces appels ne sont pas réentrants.
        static readonly Dictionary<long, string> _userNames = new();
        static readonly Dictionary<long, string> _groupNames = new();
        static readonly object _ownerLock = new();

        static string CachedName(Dictionary<long, string> cache, long id, Func<long, string> resolve)
        {
            lock (_ownerLock)
            {
                if (cache.TryGetValue(id, out string name))
                    return name;
                try { name = resolve(id); }
                catch (Exception) { name = id.ToString(); } // compte inconnu : l'identifiant brut
                cache[id] = name;
                return name;
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
                string user = CachedName(_userNames, fileInfo.OwnerUserId, uid => new UnixUserInfo(uid).UserName);
                string group = CachedName(_groupNames, fileInfo.OwnerGroupId, gid => new UnixGroupInfo(gid).GroupName);
                string result = $"{user}:{group}";
                if (verbose)
                    OutputHelper.WriteLine($"[LOCAL-OWNER] Linux owner: {result} for {Path.GetFileName(filePath)}", 3);
                return result;
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteLine($"[LOCAL-OWNER] Error getting Linux owner for {Path.GetFileName(filePath)}: {ex.Message}", 3);
                return $"<LINUX_ERROR_{ex.GetType().Name}>";
            }
        }
    }
}
