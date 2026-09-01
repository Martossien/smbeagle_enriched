using System;

namespace SMBeagle.FileDiscovery
{
    class File
    {
        public Directory ParentDirectory { get; set; }
        public string FullName { get; set; }
        public string Name { get; set; }
        public string Extension { get; set; }
        public bool Readable { get; set; }
        public bool Writeable { get; set; }
        public bool Deletable { get; set; }
        /// <summary>Taille en octets, ou <c>null</c> si elle n'a pas été collectée (sans <c>--sizefile</c>).
        /// La distinction est vitale : rendue « 0 », une taille non collectée fait exclure
        /// tout le partage comme « fichiers trop petits », sans un mot.</summary>
        public long? FileSize { get; set; }
        public DateTime AccessTime { get; set; }
        public string FileAttributes { get; set; }
        public string Owner { get; set; }
        public string FastHash { get; set; }
        public string FileSignature { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }

        public File(string name, string fullName, string extension, DateTime creationTime, DateTime lastWriteTime, Directory parentDirectory, long? fileSize = null, DateTime accessTime = default, string fileAttributes = "", string owner = "", string fastHash = "", string fileSignature = "")
        {
            Name = name;
            Extension = extension;
            CreationTime = creationTime;
            LastWriteTime = lastWriteTime;
            ParentDirectory = parentDirectory;
            FullName = fullName;
            FileSize = fileSize;
            AccessTime = accessTime;
            FileAttributes = fileAttributes;
            Owner = owner;
            FastHash = fastHash;
            FileSignature = fileSignature;
        }

        public void SetPermissions(bool read, bool write, bool delete)
        {
            Readable = read;
            Writeable = write;
            Deletable = delete;
        }

        public void SetPermissionsFromACL(ACL acl)
        {
            Readable = acl.Readable;
            Writeable = acl.Writeable;
            Deletable = acl.Deletable;
        }
    }
}
