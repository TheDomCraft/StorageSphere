using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace StorageSphere
{
    public class ArchiveManager
    {
        private readonly bool _verbose;
        private readonly bool _quiet;

        public ArchiveManager(bool verbose, bool quiet)
        {
            _verbose = verbose;
            _quiet = quiet;
        }

        // PACK: create new archive, with optional encryption, compression, hint
        public void Pack(string archive, string[] items, bool setPassword, string hint, string compression)
        {
            if (!_quiet)
                Console.WriteLine($"[StorageSphere] Creating archive '{archive}'...");

            CompressionType compType = ParseCompression(compression);

            // Set up encryption
            byte[] salt = new byte[CryptoHelper.SaltSize];
            byte[] iv = new byte[CryptoHelper.IvSize];
            byte[] key = null;
            byte[] hmacKey = null;
            bool encrypt = setPassword;
            string password = null;

            if (encrypt)
            {
                password = CryptoHelper.PromptPassword("Enter password to protect the archive: ");
                if (string.IsNullOrWhiteSpace(password))
                {
                    Console.WriteLine("[StorageSphere] Error: Password cannot be empty.");
                    return;
                }
                string confirm = CryptoHelper.PromptPassword("Confirm password: ");
                if (password != confirm)
                {
                    Console.WriteLine("[StorageSphere] Error: Passwords do not match.");
                    return;
                }
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                    rng.GetBytes(iv);
                }
                key = CryptoHelper.DeriveKey(password, salt);
                hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
            }
            else
            {
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                    rng.GetBytes(iv);
                    key = new byte[CryptoHelper.KeySize];
                    rng.GetBytes(key);
                    hmacKey = new byte[CryptoHelper.KeySize];
                    rng.GetBytes(hmacKey);
                }
            }

            using (var fs = File.Create(archive))
            using (var headerWriter = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                // Write header
                headerWriter.Write((int)0x53535048); // Magic "SSPH"
                headerWriter.Write((byte)2); // Version
                headerWriter.Write((byte)compType);
                headerWriter.Write(encrypt); // encrypted
                headerWriter.Write(hint ?? "");
                headerWriter.Write(salt);
                headerWriter.Write(iv);

                long headerEnd = fs.Position;

                // Prepare for HMAC calculation
                using (var hmacStream = new MemoryStream())
                {
                    // Write the data body to hmacStream
                    Stream dataStream = hmacStream;
                    CryptoStream cryptoStream = null;
                    if (encrypt)
                    {
                        var aes = Aes.Create();
                        aes.Key = key;
                        aes.IV = iv;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        cryptoStream = new CryptoStream(hmacStream, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                        dataStream = cryptoStream;
                    }

                    using (var writer = new BinaryWriter(dataStream, Encoding.UTF8, leaveOpen: true))
                    {
                        // Gather all files/dirs to add
                        var allEntries = new List<(string abs, string rel)>();
                        foreach (string item in items)
                        {
                            string absPath = Path.GetFullPath(item);
                            if (Directory.Exists(absPath))
                            {
                                foreach (var entry in Directory.EnumerateFileSystemEntries(absPath, "*", SearchOption.AllDirectories))
                                {
                                    string relPath = GetRelativePath(absPath, entry);
                                    allEntries.Add((entry, relPath));
                                }
                                // Add the root dir itself (may be empty)
                                allEntries.Add((absPath, "."));
                            }
                            else if (File.Exists(absPath))
                            {
                                allEntries.Add((absPath, Path.GetFileName(absPath)));
                            }
                            else
                            {
                                if (!_quiet)
                                    Console.WriteLine($"[StorageSphere] Warning: '{item}' not found.");
                            }
                        }
                        int total = allEntries.Count;
                        using (var pb = _verbose && !_quiet ? new ProgressBar(total) : null)
                        {
                            int idx = 0;
                            foreach (var (abs, rel) in allEntries)
                            {
                                idx++;
                                var fi = new FileInfo(abs);
                                bool isDir = Directory.Exists(abs);
                                EntryType type = isDir ? EntryType.Directory : EntryType.File;

                                writer.Write((byte)type);
                                writer.Write(rel);

                                var meta = GatherMetadata(abs, type);
                                meta.Write(writer);

                                if (type == EntryType.File)
                                {
                                    // Read file, compress, write
                                    using (var fileStream = File.OpenRead(abs))
                                    using (var ms = new MemoryStream())
                                    {
                                        Stream compStream = ms;
                                        switch (compType)
                                        {
                                            case CompressionType.Deflate:
                                                compStream = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.GZip:
                                                compStream = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.Brotli:
                                                compStream = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.None:
                                                break;
                                        }
                                        fileStream.CopyTo(compStream);
                                        compStream.Flush();
                                        if (compStream != ms) compStream.Dispose();

                                        byte[] compData = ms.ToArray();
                                        writer.Write((long)fi.Length);
                                        writer.Write((int)compData.Length);
                                        writer.Write(compData);
                                    }
                                }
                                else if (type == EntryType.Directory)
                                {
                                    // No data
                                }
                                pb?.Report(idx);
                            }
                        }
                    }
                    if (encrypt)
                        cryptoStream?.FlushFinalBlock();

                    // Write HMAC
                    byte[] hmac = CryptoHelper.ComputeHmac(hmacKey, hmacStream);
                    fs.Write(hmacStream.GetBuffer(), 0, (int)hmacStream.Length);
                    fs.Write(hmac, 0, hmac.Length);
                }
            }
            if (!_quiet)
                Console.WriteLine($"[StorageSphere] Archive saved: {archive}");
        }

        // UNPACK: extract all files
        public void Unpack(string archive, string outdir)
        {
            if (!_quiet)
                Console.WriteLine($"[StorageSphere] Unpacking '{archive}'...");

            using (var fs = File.OpenRead(archive))
            using (var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                int magic = reader.ReadInt32();
                if (magic != 0x53535048) throw new InvalidDataException("Not a StorageSphere archive.");
                byte version = reader.ReadByte();
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                if (!_quiet && encrypted && !string.IsNullOrWhiteSpace(hint))
                    Console.WriteLine($"Password hint: {hint}");

                byte[] key = null;
                byte[] hmacKey = null;
                if (encrypted)
                {
                    string password = CryptoHelper.PromptPassword("Enter password to decrypt archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                    hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    hmacKey = new byte[CryptoHelper.KeySize];
                    fs.Read(key, 0, key.Length);
                    fs.Read(hmacKey, 0, hmacKey.Length);
                }

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                // Verify HMAC
                fs.Position = bodyStart;
                var ms = new MemoryStream();
                fs.CopyTo(ms, (int)dataLen);
                byte[] computedHmac = CryptoHelper.ComputeHmac(hmacKey, ms);
                fs.Position = hmacPos;
                byte[] fileHmac = reader.ReadBytes(CryptoHelper.HmacSize);
                if (!CryptoHelper.VerifyHmac(hmacKey, ms, fileHmac))
                {
                    Console.WriteLine("[StorageSphere] ERROR: Archive integrity check failed (HMAC mismatch)!");
                    return;
                }

                // Decrypt if needed
                Stream dataStream = new MemoryStream(ms.GetBuffer(), 0, (int)ms.Length);
                if (encrypted)
                {
                    var aes = Aes.Create();
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    dataStream = new CryptoStream(dataStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                }

                using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false))
                {
                    var entries = new List<(EntryType type, string rel, FileMetadata meta, long origLen, int compLen, long dataPos)>();
                    while (dataStream.Position < dataStream.Length)
                    {
                        EntryType type = (EntryType)dataReader.ReadByte();
                        string rel = dataReader.ReadString();
                        var meta = FileMetadata.Read(dataReader);
                        long origLen = 0;
                        int compLen = 0;
                        long dataPos = dataStream.Position;

                        if (type == EntryType.File)
                        {
                            origLen = dataReader.ReadInt64();
                            compLen = dataReader.ReadInt32();
                            dataStream.Position += compLen;
                        }
                        entries.Add((type, rel, meta, origLen, compLen, dataPos));
                    }

                    using (var pb = _verbose && !_quiet ? new ProgressBar(entries.Count) : null)
                    {
                        int idx = 0;
                        foreach (var entry in entries)
                        {
                            idx++;
                            string outPath = Path.Combine(outdir, entry.rel);
                            if (entry.type == EntryType.Directory)
                            {
                                Directory.CreateDirectory(outPath);
                                SetMetadata(outPath, entry.meta);
                            }
                            else if (entry.type == EntryType.File)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                                dataStream.Position = entry.dataPos;
                                byte[] compData = dataReader.ReadBytes(entry.compLen);
                                using (var compStream = new MemoryStream(compData))
                                using (var fsOut = File.Create(outPath))
                                {
                                    Stream decStream = compStream;
                                    switch (compType)
                                    {
                                        case CompressionType.Deflate:
                                            decStream = new DeflateStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.GZip:
                                            decStream = new GZipStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.Brotli:
                                            decStream = new BrotliStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.None:
                                            break;
                                    }
                                    decStream.CopyTo(fsOut);
                                }
                                SetMetadata(outPath, entry.meta);
                            }
                            pb?.Report(idx);
                        }
                    }
                }
            }
            if (!_quiet)
                Console.WriteLine($"[StorageSphere] Unpacked archive to '{outdir}'.");
        }

        // EXTRACT SINGLE FILE
        public void ExtractSingle(string archive, string entry, string outFile)
        {
            if (!_quiet)
                Console.WriteLine($"[StorageSphere] Extracting '{entry}' from '{archive}'...");

            using (var fs = File.OpenRead(archive))
            using (var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                int magic = reader.ReadInt32();
                if (magic != 0x53535048) throw new InvalidDataException("Not a StorageSphere archive.");
                byte version = reader.ReadByte();
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                byte[] key = null;
                if (encrypted)
                {
                    if (!_quiet && !string.IsNullOrWhiteSpace(hint))
                        Console.WriteLine($"Password hint: {hint}");
                    string password = CryptoHelper.PromptPassword("Enter password to decrypt archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    fs.Read(key, 0, key.Length);
                }

                // Read only encrypted data section
                fs.Position = bodyStart;
                byte[] encryptedData = new byte[dataLen];
                fs.Read(encryptedData, 0, (int)dataLen);
                Stream dataStream = new MemoryStream(encryptedData);
                if (encrypted)
                {
                    var aes = Aes.Create();
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    dataStream = new CryptoStream(dataStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                }

                using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false))
                {
                    while (true)
                    {
                        EntryType type;
                        try { type = (EntryType)dataReader.ReadByte(); }
                        catch (EndOfStreamException) { break; }
                        string rel = dataReader.ReadString();
                        var meta = FileMetadata.Read(dataReader);
                        if (type == EntryType.File)
                        {
                            long origLen = dataReader.ReadInt64();
                            int compLen = dataReader.ReadInt32();
                            if (rel == entry)
                            {
                                byte[] compData = dataReader.ReadBytes(compLen);
                                using (var compStream = new MemoryStream(compData))
                                using (var fsOut = File.Create(outFile))
                                {
                                    Stream decStream = compStream;
                                    switch (compType)
                                    {
                                        case CompressionType.Deflate:
                                            decStream = new DeflateStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.GZip:
                                            decStream = new GZipStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.Brotli:
                                            decStream = new BrotliStream(compStream, CompressionMode.Decompress);
                                            break;
                                        case CompressionType.None:
                                            break;
                                    }
                                    decStream.CopyTo(fsOut);
                                }
                                SetMetadata(outFile, meta);
                                if (!_quiet)
                                    Console.WriteLine($"[StorageSphere] Extracted '{entry}' to '{outFile}'.");
                                return;
                            }
                            else
                            {
                                // skip
                                dataReader.BaseStream.Seek(compLen, SeekOrigin.Current);
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"[StorageSphere] Entry '{entry}' not found in archive.");
        }

        // LIST CONTENTS: FIXED to only decrypt the correct section
        public void ListContents(string archive)
        {
            using (var fs = File.OpenRead(archive))
            using (var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                int magic = reader.ReadInt32();
                if (magic != 0x53535048) throw new InvalidDataException("Not a StorageSphere archive.");
                byte version = reader.ReadByte();
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                byte[] key = null;
                if (encrypted)
                {
                    if (!_quiet && !string.IsNullOrWhiteSpace(hint))
                        Console.WriteLine($"Password hint: {hint}");
                    string password = CryptoHelper.PromptPassword("Enter password to list archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    fs.Read(key, 0, key.Length);
                }

                // Read only encrypted data section
                fs.Position = bodyStart;
                byte[] encryptedData = new byte[dataLen];
                fs.Read(encryptedData, 0, (int)dataLen);
                Stream dataStream = new MemoryStream(encryptedData);
                if (encrypted)
                {
                    var aes = Aes.Create();
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    dataStream = new CryptoStream(dataStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                }

                using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false))
                {
                    while (true)
                    {
                        EntryType type;
                        try { type = (EntryType)dataReader.ReadByte(); }
                        catch (EndOfStreamException) { break; }
                        catch (IOException) { break; }
                        string rel = dataReader.ReadString();
                        var meta = FileMetadata.Read(dataReader);
                        string typeStr = type == EntryType.Directory ? "[DIR ]" : "[FILE]";
                        string perms = meta.UnixPerms ?? "";
                        long origLen = 0;
                        int compLen = 0;
                        if (type == EntryType.File)
                        {
                            origLen = dataReader.ReadInt64();
                            compLen = dataReader.ReadInt32();
                            byte[] discardBuf = new byte[8192];
                            int bytesToRead = compLen;
                            while (bytesToRead > 0)
                            {
                                int chunk = Math.Min(discardBuf.Length, bytesToRead);
                                int read = dataReader.BaseStream.Read(discardBuf, 0, chunk);
                                if (read == 0) break;
                                bytesToRead -= read;
                            }
                        }
                        Console.WriteLine($"{typeStr} {rel} perms={perms}");
                    }
                }
            }
        }

        // SHOW INFO
        public void ShowInfo(string archive)
        {
            using (var fs = File.OpenRead(archive))
            using (var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                int magic = reader.ReadInt32();
                if (magic != 0x53535048) throw new InvalidDataException("Not a StorageSphere archive.");
                byte version = reader.ReadByte();
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                int fileCount = 0, dirCount = 0;
                long origTotal = 0, compTotal = 0;
                byte[] key = null;
                if (encrypted)
                {
                    string password = CryptoHelper.PromptPassword("Enter password to show info: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    fs.Read(key, 0, key.Length);
                }

                fs.Position = bodyStart;
                byte[] encryptedData = new byte[dataLen];
                fs.Read(encryptedData, 0, (int)dataLen);
                Stream dataStream = new MemoryStream(encryptedData);
                if (encrypted)
                {
                    var aes = Aes.Create();
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    dataStream = new CryptoStream(dataStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                }

                using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false))
                {
                    while (true)
                    {
                        EntryType type;
                        try { type = (EntryType)dataReader.ReadByte(); }
                        catch (EndOfStreamException) { break; }
                        catch (IOException) { break; }
                        string rel = dataReader.ReadString();
                        var meta = FileMetadata.Read(dataReader);
                        if (type == EntryType.File)
                        {
                            fileCount++;
                            long origLen = dataReader.ReadInt64();
                            int compLen = dataReader.ReadInt32();
                            origTotal += origLen;
                            compTotal += compLen;
                            dataReader.BaseStream.Seek(compLen, SeekOrigin.Current);
                        }
                        else if (type == EntryType.Directory)
                        {
                            dirCount++;
                        }
                    }
                }

                Console.WriteLine("===== StorageSphere Archive Info =====");
                Console.WriteLine($"Archive: {archive}");
                Console.WriteLine($"Version: {version}");
                Console.WriteLine($"Compression: {compType}");
                Console.WriteLine($"Encrypted: {encrypted}");
                if (!string.IsNullOrWhiteSpace(hint))
                    Console.WriteLine($"Password hint: {hint}");
                Console.WriteLine($"Files: {fileCount}, Dirs: {dirCount}");
                Console.WriteLine($"Total size: {Utils.HumanSize(origTotal)} ({origTotal} bytes)");
                Console.WriteLine($"Compressed: {Utils.HumanSize(compTotal)} ({compTotal} bytes)");
                Console.WriteLine("===================================");
            }
        }

        // CHANGE PASSWORD
        public void ChangePassword(string archive)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "storsphere_passwd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Unpack(archive, tempDir);
                Console.WriteLine("[StorageSphere] Set new password for archive.");
                string newPass = CryptoHelper.PromptPassword("New password: ");
                if (string.IsNullOrWhiteSpace(newPass))
                {
                    Console.WriteLine("[StorageSphere] Error: Password cannot be empty.");
                    return;
                }
                string confirm = CryptoHelper.PromptPassword("Confirm password: ");
                if (newPass != confirm)
                {
                    Console.WriteLine("[StorageSphere] Error: Passwords do not match.");
                    return;
                }
                string[] allFiles = Directory.GetFileSystemEntries(tempDir, "*", SearchOption.AllDirectories);
                Pack(archive, allFiles, true, null, "deflate");
                if (!_quiet)
                    Console.WriteLine($"[StorageSphere] Archive password changed.");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // HELPERS
        private CompressionType ParseCompression(string c)
        {
            return c?.ToLowerInvariant() switch
            {
                "none" => CompressionType.None,
                "gzip" => CompressionType.GZip,
                "brotli" => CompressionType.Brotli,
                _ => CompressionType.Deflate
            };
        }

        private string GetRelativePath(string root, string full)
        {
            var rootUri = new Uri(Path.GetFullPath(root) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(Path.GetFullPath(full));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private FileMetadata GatherMetadata(string path, EntryType type)
        {
            var info = new FileInfo(path);
            var meta = new FileMetadata
            {
                LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                CreationTimeUtc = info.Exists ? info.CreationTimeUtc.Ticks : 0,
                LastAccessTimeUtc = info.Exists ? info.LastAccessTimeUtc.Ticks : 0,
                Attributes = info.Exists ? (int)info.Attributes : 0,
                UnixPerms = Utils.GetUnixPermissions(path),
                Owner = "",
                Group = ""
            };
            return meta;
        }

        private void SetMetadata(string path, FileMetadata meta)
        {
            try
            {
                if (meta.LastWriteTimeUtc != 0)
                    File.SetLastWriteTimeUtc(path, new DateTime(meta.LastWriteTimeUtc, DateTimeKind.Utc));
                if (meta.CreationTimeUtc != 0)
                    File.SetCreationTimeUtc(path, new DateTime(meta.CreationTimeUtc, DateTimeKind.Utc));
                if (meta.LastAccessTimeUtc != 0)
                    File.SetLastAccessTimeUtc(path, new DateTime(meta.LastAccessTimeUtc, DateTimeKind.Utc));
                File.SetAttributes(path, (FileAttributes)meta.Attributes);
                if (!string.IsNullOrEmpty(meta.UnixPerms))
                    Utils.SetUnixPermissions(path, meta.UnixPerms);
            }
            catch { }
        }

        public void AddFiles(string archive, string[] files)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "storsphere_add_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                Unpack(archive, tempDir);

                foreach (string f in files)
                {
                    string abs = Path.GetFullPath(f);
                    string dest = Path.Combine(tempDir, Path.GetFileName(f));
                    if (Directory.Exists(abs))
                    {
                        CopyDirectory(abs, dest);
                    }
                    else if (File.Exists(abs))
                    {
                        File.Copy(abs, dest, true);
                    }
                }

                // Repack
                string archiveBak = archive + ".bak";
                File.Move(archive, archiveBak);
                string[] allFiles = Directory.GetFileSystemEntries(tempDir, "*", SearchOption.AllDirectories);
                Pack(archive, allFiles, false, null, "deflate");
                File.Delete(archiveBak);

                if (!_quiet)
                    Console.WriteLine($"[StorSphere] Added files to archive.");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // Helper (place in the class if not present):
        private void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            }
            foreach (string dir in Directory.GetDirectories(src))
            {
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }
        }
    }
}