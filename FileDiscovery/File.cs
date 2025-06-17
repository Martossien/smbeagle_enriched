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
        public long FileSize { get; set; }
        public DateTime AccessTime { get; set; }
        public string FileAttributes { get; set; }
        public string Owner { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }

        public File(string name, string fullName, string extension, DateTime creationTime, DateTime lastWriteTime, Directory parentDirectory, long fileSize = 0, DateTime accessTime = default, string fileAttributes = "", string owner = "")
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
