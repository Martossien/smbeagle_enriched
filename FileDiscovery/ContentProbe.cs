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
    /// hash rapide (xxHash64) et la signature (nombres magiques). Une seule
    /// implémentation, paramétrée par un lecteur : fichier local (Windows ou
    /// Linux) ou SMB via SMBLibrary.
    /// Si le fichier est illisible les deux valeurs sont vides : docia ignore un
    /// FastHash vide pour ses familles de doublons, un marqueur textuel les
    /// fausserait. Un échec de la seule détection de signature ne touche pas
    /// au hash.
    /// </summary>
    static class ContentProbe
    {
        public const int FAST_HASH_BYTES = 65536;
        /// <summary>Repli pour la signature : nombres magiques seuls, sans structure.</summary>
        public const int SIGNATURE_BYTES = 32;
        /// <summary>En-tête OLE2 / Compound File Binary (doc, xls, ppt, msg, vsd).</summary>
        static readonly byte[] OLE2_MAGIC = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        public readonly record struct Result(string FastHash, string FileSignature)
        {
            public static readonly Result Empty = new(string.Empty, string.Empty);
        }

        /// <summary>Hash et signature à partir d'un lecteur rendant au plus n octets d'en-tête.</summary>
        public static Result Probe(Func<int, byte[]> readHead, bool wantHash, bool wantSignature)
        {
            if (!wantHash && !wantSignature)
                return Result.Empty;
            // La signature aussi a besoin de l'en-tête complet : les formats OLE2 (doc,
            // xls, ppt) et zip (docx, xlsx, odt) s'identifient par leur structure.
            byte[] head = readHead(FAST_HASH_BYTES) ?? Array.Empty<byte>();
            string hash = wantHash ? XxHash64.HashToUInt64(head).ToString("x16") : string.Empty;
            string signature = wantSignature ? Signature(head) : string.Empty;
            return new Result(hash, signature);
        }

        /// <summary>
        /// Extension détectée (« pdf », « doc », « docx »...), « ole » pour un fichier
        /// composé OLE2 dont la structure dépasse l'en-tête lu, « unknown » sinon.
        /// Ne lève jamais : la bibliothèque FileSignatures analyse la structure des
        /// conteneurs et lève une exception sur un en-tête tronqué (CFCorruptedFileException
        /// sur 32 octets d'un .doc, CFException sur un .ppt dont le répertoire est au-delà
        /// de 64 Ko).
        /// </summary>
        public static string Signature(byte[] head)
        {
            string detected = TryInspect(head, head.Length);
            if (detected != null)
                return detected;
            if (head.Length >= OLE2_MAGIC.Length && head.AsSpan(0, OLE2_MAGIC.Length).SequenceEqual(OLE2_MAGIC))
                return "ole";
            // Repli : nombres magiques seuls (comportement historique sur 32 octets)
            return TryInspect(head, Math.Min(head.Length, SIGNATURE_BYTES)) ?? "unknown";
        }

        static string TryInspect(byte[] head, int length)
        {
            try
            {
                using MemoryStream ms = new(head, 0, length);
                var format = new FileSignatures.FileFormatInspector().DetermineFileFormat(ms);
                return format == null ? "unknown" : format.Extension.TrimStart('.').ToLower();
            }
            catch (Exception)
            {
                return null; // en-tête insuffisant pour ce format : l'appelant se replie
            }
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
