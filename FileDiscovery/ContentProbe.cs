using SMBLibrary;
using SMBLibrary.Client;
using System;
using System.IO;
using System.IO.Hashing;
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

        /// <summary>Fichier local (chemin Windows ou POSIX).</summary>
        public static Result ProbeLocal(string path, bool wantHash, bool wantSignature, bool verbose = false)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Probe(n => ReadHead(fs, n), wantHash, wantSignature);
            }
            catch (Exception ex)
            {
                if (verbose)
                    OutputHelper.WriteError($"lecture impossible de '{path}' : {ex.GetType().Name} {ex.Message}");
                return Result.Empty;
            }
        }

        /// <summary>Fichier distant via un ISMBFileStore déjà connecté au partage.</summary>
        public static Result ProbeSmb(ISMBFileStore fileStore, string path, bool wantHash, bool wantSignature, bool verbose = false)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            try
            {
                NTStatus status = fileStore.CreateFile(out object handle, out _, path, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Normal, ShareAccess.Read, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    if (verbose)
                        OutputHelper.WriteError($"ouverture SMB impossible de '{path}' : {status}");
                    return Result.Empty;
                }
                try
                {
                    return Probe(n => ReadSmbHead(fileStore, handle, n), wantHash, wantSignature);
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
