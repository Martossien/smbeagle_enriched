using SMBLibrary.Client;
using SMBeagle.Output;
using SMBLibrary;
using System;
using System.IO;

namespace SMBeagle.FileDiscovery
{
    class CrossPlatformHelper
    {
        private static bool CheckAccessMask(ISMBFileStore fileStore, string filePath, AccessMask accessMask)
        {
            if (fileStore is SMB1FileStore)
            {
                filePath = @"\\" + filePath;
            }
            object handle;
            NTStatus result = fileStore.CreateFile(out handle, out _, filePath, accessMask, SMBLibrary.FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (result == NTStatus.STATUS_SUCCESS)
                fileStore.CloseFile(handle);
            return result == NTStatus.STATUS_SUCCESS;
        }
        public static ACL ResolvePermissions(File file)
        {
            ACL acl = new();
            if (file.ParentDirectory.Share == null)
                throw new InvalidOperationException($"le fichier '{file.FullName}' n'a pas de partage parent");
            NTStatus status;
            //TODO: optimise storing filestore somewhere
            ISMBFileStore fileStore = file.ParentDirectory.Share.Host.Client.TreeConnect(file.ParentDirectory.Share.Name, out status);

            if (status != NTStatus.STATUS_SUCCESS)
            {
                OutputHelper.WriteError($"connexion au partage '{file.ParentDirectory.Share.uncPath}' impossible : {status}");
                return acl;
            }

            try
            {
                string filePath = file.FullName;
                acl.Readable = CheckAccessMask(fileStore, filePath, AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE);
                acl.Writeable = CheckAccessMask(fileStore, filePath, AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE);
                acl.Deletable = CheckAccessMask(fileStore, filePath, AccessMask.DELETE | AccessMask.SYNCHRONIZE);
            }
            finally
            {
                fileStore.Disconnect();
            }
            return acl;
        }
        public static void RetrieveFile(File file, string outputPath)
        {
            //TODO: Check file size and gate
            NTStatus status;
            ISMBFileStore fileStore = file.ParentDirectory.Share.Host.Client.TreeConnect(file.ParentDirectory.Share.Name, out status);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                OutputHelper.WriteError($"connexion au partage '{file.ParentDirectory.Share.uncPath}' impossible : {status}");
                return;
            }

            try
            {
                object handle;
                NTStatus result = fileStore.CreateFile(out handle, out _, file.FullName, AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, SMBLibrary.FileAttributes.Normal, ShareAccess.None, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
                if (result == NTStatus.STATUS_SUCCESS)
                {
                    try
                    {
                        // Open a FileStream to write chunks to the local file
                        using (FileStream localFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                        {
                            long bytesRead = 0;
                            byte[] data;

                            while (true)
                            {
                                // Read the next chunk from the remote file
                                status = fileStore.ReadFile(out data, handle, bytesRead, (int)file.ParentDirectory.Share.Host.Client.MaxReadSize);

                                // Check for read errors or end of file
                                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                                {
                                    throw new Exception("Failed to read from file. Status: " + status.ToString());
                                }

                                if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                                {
                                    // Done reading
                                    break;
                                }

                                // Write the chunk to the local file
                                localFileStream.Write(data, 0, data.Length);

                                // Update total bytes read
                                bytesRead += data.Length;
                            }
                        }
                    }
                    finally
                    {
                        // Close the remote file
                        fileStore.CloseFile(handle);
                    }
                }
            }
            finally
            {
                // Disconnect from the SMB file store
                fileStore.Disconnect();
            }
        }
    }
}
