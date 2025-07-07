using System;
using System.IO;

namespace StorageSphere
{
    public enum EntryType : byte
    {
        File = 1,
        Directory = 2
        // Symlink removed
    }

    public enum CompressionType : byte
    {
        None = 0,
        Deflate = 1,
        GZip = 2,
        Brotli = 3
    }

    public struct FileMetadata
    {
        public long LastWriteTimeUtc;
        public long CreationTimeUtc;
        public long LastAccessTimeUtc;
        public int Attributes;
        public string UnixPerms;
        // Removed IsSymlink, SymlinkTarget
        public string Owner;
        public string Group;

        public static FileMetadata Read(BinaryReader r)
        {
            var meta = new FileMetadata();
            meta.LastWriteTimeUtc = r.ReadInt64();
            meta.CreationTimeUtc = r.ReadInt64();
            meta.LastAccessTimeUtc = r.ReadInt64();
            meta.Attributes = r.ReadInt32();
            meta.UnixPerms = r.ReadString();
            // Symlink fields removed
            meta.Owner = r.ReadString();
            meta.Group = r.ReadString();
            return meta;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(LastWriteTimeUtc);
            w.Write(CreationTimeUtc);
            w.Write(LastAccessTimeUtc);
            w.Write(Attributes);
            w.Write(UnixPerms ?? "");
            // Symlink fields removed
            w.Write(Owner ?? "");
            w.Write(Group ?? "");
        }
    }
}