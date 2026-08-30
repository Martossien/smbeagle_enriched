using SMBLibrary;
using SMBLibrary.Client;
using System;
using System.IO;
using System.IO.Hashing;
using System.Threading;
using SMBeagle.Output;

namespace SMBeagle.FileDiscovery
{
    /// <summary>
    /// Lecture unique de l'en-tête d'un fichier (64 Ko max) dont on dérive le
    /// hash rapide (xxHash64) et la signature (nombres magiques, 32 premiers
    /// octets). Une seule implémentation, paramétrée par un lecteur : fichier
    /// local (Windows ou Linux) ou SMB via SMBLibrary.
    /// En cas d'erreur les deux valeurs sont vides : docia ignore un FastHash
    /// vide pour ses familles de doublons, un marqueur textuel les fausserait.
    /// </summary>
    static class ContentProbe
    {
        public const int FAST_HASH_BYTES = 65536;
        public const int SIGNATURE_BYTES = 32;

        public readonly record struct Result(string FastHash, string FileSignature)
        {
            public static readonly Result Empty = new(string.Empty, string.Empty);
        }

        /// <summary>Hash et signature à partir d'un lecteur rendant au plus n octets d'en-tête.</summary>
        public static Result Probe(Func<int, byte[]> readHead, bool wantHash, bool wantSignature)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            byte[] head = readHead(wantHash ? FAST_HASH_BYTES : SIGNATURE_BYTES) ?? Array.Empty<byte>();
            string hash = wantHash ? XxHash64.HashToUInt64(head).ToString("x16") : string.Empty;
            string signature = wantSignature ? Signature(head) : string.Empty;
            return new Result(hash, signature);
        }

        static string Signature(byte[] head)
        {
            using MemoryStream ms = new(head, 0, Math.Min(head.Length, SIGNATURE_BYTES));
            var format = new FileSignatures.FileFormatInspector().DetermineFileFormat(ms);
            return format == null ? "unknown" : format.Extension.TrimStart('.').ToLower();
        }

        public static byte[] ReadHead(Stream stream, int max)
        {
            byte[] buffer = new byte[max];
            int total = 0;
            while (total < max)
            {
                int read = stream.Read(buffer, total, max - total);
                if (read <= 0)
                    break;
                total += read;
            }
            return total == max ? buffer : buffer[..total];
        }

        static long _accessTimeRestoreFailures;

        /// <summary>Nombre de fichiers dont la date d'accès n'a pas pu être restaurée.</summary>
        public static long AccessTimeRestoreFailures => Interlocked.Read(ref _accessTimeRestoreFailures);

        /// <summary>
        /// Fichier local (chemin Windows ou POSIX). Si <paramref name="restoreAccessTimeUtc"/>
        /// est fourni, la date de dernier accès est remise à cette valeur après lecture
        /// (nécessite le droit d'écrire les attributs ; sous Linux, d'être propriétaire).
        /// </summary>
        public static Result ProbeLocal(string path, bool wantHash, bool wantSignature, bool verbose = false, DateTime? restoreAccessTimeUtc = null)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            Result result;
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                result = Probe(n => ReadHead(fs, n), wantHash, wantSignature);
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteError($"lecture impossible de '{path}' : {ex.GetType().Name} {ex.Message}");
                return Result.Empty;
            }
            if (restoreAccessTimeUtc.HasValue)
            {
                try
                {
                    System.IO.File.SetLastAccessTimeUtc(path, restoreAccessTimeUtc.Value);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _accessTimeRestoreFailures);
                    if (verbose)
                        OutputHelper.WriteError($"date d'accès non restaurée pour '{path}' : {ex.GetType().Name} {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>
        /// Fichier distant via un ISMBFileStore déjà connecté au partage. Si
        /// <paramref name="restoreAccessTime"/> est fourni, le fichier est ouvert avec
        /// FILE_WRITE_ATTRIBUTES et la date d'accès est remise à cette valeur
        /// (SetFileInformation / FileBasicInformation) avant fermeture du handle ;
        /// sans ce droit on lit quand même et l'échec est compté.
        /// </summary>
        public static Result ProbeSmb(ISMBFileStore fileStore, string path, bool wantHash, bool wantSignature, bool verbose = false, DateTime? restoreAccessTime = null)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            try
            {
                bool canRestore = false;
                object handle = null;
                NTStatus status = NTStatus.STATUS_ACCESS_DENIED;
                if (restoreAccessTime.HasValue)
                {
                    status = OpenForRead(fileStore, path, AccessMask.GENERIC_READ | (AccessMask)FileAccessMask.FILE_WRITE_ATTRIBUTES, out handle);
                    canRestore = status == NTStatus.STATUS_SUCCESS;
                    if (!canRestore)
                        Interlocked.Increment(ref _accessTimeRestoreFailures);
                }
                if (!canRestore)
                    status = OpenForRead(fileStore, path, AccessMask.GENERIC_READ, out handle);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    if (verbose)
                        OutputHelper.WriteError($"ouverture SMB impossible de '{path}' : {status}");
                    return Result.Empty;
                }
                try
                {
                    Result result = Probe(n => ReadSmbHead(fileStore, handle, n), wantHash, wantSignature);
                    if (canRestore)
                    {
                        var info = new FileBasicInformation { LastAccessTime = new SetFileTime(restoreAccessTime) };
                        NTStatus restored = fileStore.SetFileInformation(handle, info);
                        if (restored != NTStatus.STATUS_SUCCESS)
                        {
                            Interlocked.Increment(ref _accessTimeRestoreFailures);
                            if (verbose)
                                OutputHelper.WriteError($"date d'accès SMB non restaurée pour '{path}' : {restored}");
                        }
                    }
                    return result;
                }
                finally
                {
                    fileStore.CloseFile(handle);
                }
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteError($"lecture SMB impossible de '{path}' : {ex.GetType().Name} {ex.Message}");
                return Result.Empty;
            }
        }

        static NTStatus OpenForRead(ISMBFileStore fileStore, string path, AccessMask access, out object handle)
        {
            return fileStore.CreateFile(out handle, out _, path, access, SMBLibrary.FileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        }

        static byte[] ReadSmbHead(ISMBFileStore fileStore, object handle, int max)
        {
            using MemoryStream ms = new();
            while (ms.Length < max)
            {
                NTStatus status = fileStore.ReadFile(out byte[] data, handle, ms.Length, (int)(max - ms.Length));
                if (status == NTStatus.STATUS_END_OF_FILE || data == null || data.Length == 0)
                    break;
                if (status != NTStatus.STATUS_SUCCESS)
                    throw new IOException($"ReadFile : {status}");
                ms.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }
    }
}
