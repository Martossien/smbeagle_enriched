﻿using SMBeagle.Output;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMBeagle.FileDiscovery.Output
{
    class FileOutput : IOutputPayload
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public string Extension { get; set; }
        public string Username { get; set; }
        public string Hostname { get; set; }
        public string UNCDirectory { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public bool Readable { get; set; }
        public bool Writeable { get; set; }
        public bool Deletable { get; set; }
        public Enums.DirectoryTypeEnum DirectoryType { get; set; }
        public string Base { get; set; }
        public long FileSize { get; set; }
        public DateTime AccessTime { get; set; }
        public string FileAttributes { get; set; }
        public string Owner { get; set; }
        public string FastHash { get; set; }
        public string FileSignature { get; set; }
        public FileOutput(File file)
        {
            // Preserve original case for filename in CSV output
            Name = file.Name;
            Host = file.ParentDirectory.Share.Host.Address;
            Extension = file.Extension.TrimStart('.').ToLower();
            // Preserve original case for UNC path in CSV output
            UNCDirectory = file.ParentDirectory.UNCPath;
            CreationTime = file.CreationTime;
            LastWriteTime = file.LastWriteTime;
            Readable = file.Readable;
            Writeable = file.Writeable;
            Deletable = file.Deletable;
            DirectoryType = file.ParentDirectory.Base.DirectoryType;
            Base = file.ParentDirectory.Share.uncPath;
            FileSize = file.FileSize;
            AccessTime = file.AccessTime;
            FileAttributes = file.FileAttributes;
            Owner = file.Owner;
            FastHash = file.FastHash;
            FileSignature = file.FileSignature;
        }
    }
}
