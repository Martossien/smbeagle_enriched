using System;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace SMBeagle.FileDiscovery
{
    static class LocalHelper
    {
        public static ACL ResolvePermissions(string path)
        {
            ACL acl = new();
            try { new FileStream(path, FileMode.Open, FileAccess.Read).Dispose(); acl.Readable = true; } catch { }
            try { new FileStream(path, FileMode.Open, FileAccess.Write).Dispose(); acl.Writeable = true; } catch { }
            // Deletion test is not performed for safety
            return acl;
        }

        public static string ComputeFastHash(string filePath)
        {
            const int READ_SIZE = 65536;
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int toRead = (int)Math.Min(READ_SIZE, fs.Length);
                byte[] buffer = new byte[toRead];
                int read = fs.Read(buffer, 0, toRead);
                ulong hash = XxHash64.HashToUInt64(buffer.AsSpan(0, read));
                return hash.ToString("x16");
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string DetectFileSignature(string filePath)
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
                return format == null ? "unknown" : format.Extension.TrimStart('.').ToLower();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetFileOwner(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#pragma warning disable CA1416
                return WindowsHelper.GetFileOwner(filePath);
#pragma warning restore CA1416
            }
            return "<NOT_SUPPORTED>";
        }
    }
}
