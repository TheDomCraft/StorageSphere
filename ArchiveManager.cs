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
                }
                key = new byte[CryptoHelper.KeySize]; // unused
                hmacKey = new byte[CryptoHelper.KeySize]; // will use fixed all-zero key
                Array.Clear(hmacKey, 0, hmacKey.Length);
            }

            using (var fs = File.Create(archive))
            using (var headerWriter = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                // Write header
                headerWriter.Write((int)0x53535048); // Magic "SSPH"
                headerWriter.Write((byte)3); // Version: UPDATED TO 3
                headerWriter.Write((byte)compType);
                headerWriter.Write(encrypt); // encrypted
                headerWriter.Write(hint ?? "");
                headerWriter.Write(salt);
                headerWriter.Write(iv);

                long headerEnd = fs.Position;

                // Prepare for HMAC calculation
                using (var hmacStream = new MemoryStream())
                {
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
                        var allEntries = new List<(string abs, string rel)>();
                        foreach (string item in items)
                        {
                            string absPath = Path.GetFullPath(item);
                            if (Directory.Exists(absPath))
                            {
                                string rootName = Path.GetFileName(Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                foreach (var entry in Directory.EnumerateFileSystemEntries(absPath, "*", SearchOption.AllDirectories))
                                {
                                    string relPath = GetSafeRelativePath(absPath, entry);
                                    relPath = Path.Combine(rootName, relPath);
                                    allEntries.Add((entry, relPath));
                                }
                                allEntries.Add((absPath, rootName));
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
                        long total = allEntries.Count;
                        using (var pb = _verbose && !_quiet ? new ProgressBar((int)Math.Min(total, int.MaxValue)) : null)
                        {
                            long idx = 0;
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
                                    using (var fileStream = File.OpenRead(abs))
                                    {
                                        writer.Write((long)fi.Length);

                                        // Placeholder for compressed length (long)
                                        long compLenPos = dataStream.Position;
                                        writer.Write((long)0); // placeholder
                                        long dataStartPos = dataStream.Position;

                                        Stream outStream = dataStream;
                                        Stream compStream = outStream;
                                        switch (compType)
                                        {
                                            case CompressionType.Deflate:
                                                compStream = new DeflateStream(outStream, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.GZip:
                                                compStream = new GZipStream(outStream, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.Brotli:
                                                compStream = new BrotliStream(outStream, CompressionLevel.Optimal, leaveOpen: true);
                                                break;
                                            case CompressionType.None:
                                                break;
                                        }
                                        fileStream.CopyTo(compStream);
                                        compStream.Flush();
                                        if (compStream != outStream) compStream.Dispose();

                                        long dataEndPos = dataStream.Position;
                                        long compLen = dataEndPos - dataStartPos;

                                        // Seek back and write the length
                                        long currPos = dataStream.Position;
                                        dataStream.Position = compLenPos;
                                        writer.Write(compLen);
                                        dataStream.Position = currPos;
                                    }
                                }
                                pb?.Report((int)Math.Min(idx, int.MaxValue));
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
                if (version < 3)
                    throw new InvalidDataException($"Unsupported archive version: {version}. Version 3+ required for large file support.");
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                byte[] key = null;
                byte[] hmacKey = null;
                if (encrypted)
                {
                    if (!_quiet && !string.IsNullOrWhiteSpace(hint))
                        Console.WriteLine($"Password hint: {hint}");
                    string password = CryptoHelper.PromptPassword("Enter password to decrypt archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                    hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    hmacKey = new byte[CryptoHelper.KeySize];
                    Array.Clear(hmacKey, 0, hmacKey.Length);
                }

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                fs.Position = bodyStart;
                byte[] dataSection = new byte[dataLen];
                fs.Read(dataSection, 0, (int)dataLen);

                byte[] computedHmac = CryptoHelper.ComputeHmac(hmacKey, new MemoryStream(dataSection));
                fs.Position = hmacPos;
                byte[] fileHmac = reader.ReadBytes(CryptoHelper.HmacSize);
                if (!CryptoHelper.VerifyHmac(hmacKey, new MemoryStream(dataSection), fileHmac))
                {
                    Console.WriteLine("[StorageSphere] ERROR: Archive integrity check failed (HMAC mismatch)!");
                    return;
                }

                Stream dataStream = new MemoryStream(dataSection, 0, dataSection.Length);
                if (encrypted)
                {
                    var aes = Aes.Create();
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    dataStream = new CryptoStream(dataStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
                }

                long totalCount = 0;
                if (_verbose && !_quiet)
                {
                    if (dataStream is MemoryStream ms)
                    {
                        long oldPos = ms.Position;
                        var tempReader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
                        while (true)
                        {
                            try
                            {
                                EntryType type = (EntryType)tempReader.ReadByte();
                                tempReader.ReadString();
                                FileMetadata.Read(tempReader);
                                if (type == EntryType.File)
                                {
                                    tempReader.ReadInt64();
                                    long compLen = tempReader.ReadInt64();
                                    tempReader.BaseStream.Position += compLen;
                                }
                                totalCount++;
                            }
                            catch
                            {
                                break;
                            }
                        }
                        ms.Position = oldPos;
                    }
                }

                using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false))
                using (var pb = _verbose && !_quiet ? new ProgressBar((int)Math.Min(totalCount, int.MaxValue)) : null)
                {
                    long idx = 0;
                    while (true)
                    {
                        EntryType type;
                        try { type = (EntryType)dataReader.ReadByte(); }
                        catch (EndOfStreamException) { break; }
                        catch (IOException) { break; }
                        string rel = dataReader.ReadString();

                        if (Path.IsPathRooted(rel) || rel.Contains(".."))
                            throw new InvalidDataException("Invalid path in archive: " + rel);

                        var meta = FileMetadata.Read(dataReader);
                        string outPath = Path.Combine(outdir, rel);

                        if (type == EntryType.Directory)
                        {
                            Directory.CreateDirectory(outPath);
                            SetMetadata(outPath, meta);
                        }
                        else if (type == EntryType.File)
                        {
                            long origLen = dataReader.ReadInt64();
                            long compLen = dataReader.ReadInt64();

                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            using (var fsOut = File.Create(outPath))
                            {
                                Stream compStream = new SubStream(dataReader.BaseStream, compLen);
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
                                if (decStream != compStream)
                                    decStream.Dispose();
                                compStream.Dispose();
                            }
                            SetMetadata(outPath, meta);
                        }
                        idx++;
                        pb?.Report((int)Math.Min(idx, int.MaxValue));
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
                if (version < 3)
                    throw new InvalidDataException($"Unsupported archive version: {version}. Version 3+ required for large file support.");
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                byte[] key = null;
                byte[] hmacKey = null;
                if (encrypted)
                {
                    if (!_quiet && !string.IsNullOrWhiteSpace(hint))
                        Console.WriteLine($"Password hint: {hint}");
                    string password = CryptoHelper.PromptPassword("Enter password to decrypt archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                    hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    hmacKey = new byte[CryptoHelper.KeySize];
                    Array.Clear(hmacKey, 0, hmacKey.Length);
                }

                fs.Position = bodyStart;
                byte[] dataSection = new byte[dataLen];
                fs.Read(dataSection, 0, (int)dataLen);

                byte[] computedHmac = CryptoHelper.ComputeHmac(hmacKey, new MemoryStream(dataSection));
                fs.Position = hmacPos;
                byte[] fileHmac = reader.ReadBytes(CryptoHelper.HmacSize);
                if (!CryptoHelper.VerifyHmac(hmacKey, new MemoryStream(dataSection), fileHmac))
                {
                    Console.WriteLine("[StorageSphere] ERROR: Archive integrity check failed (HMAC mismatch)!");
                    return;
                }

                Stream dataStream = new MemoryStream(dataSection, 0, dataSection.Length);
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
                        if (Path.IsPathRooted(rel) || rel.Contains(".."))
                            throw new InvalidDataException("Invalid path in archive: " + rel);

                        var meta = FileMetadata.Read(dataReader);
                        if (type == EntryType.File)
                        {
                            long origLen = dataReader.ReadInt64();
                            long compLen = dataReader.ReadInt64();
                            if (rel == entry)
                            {
                                using (var fsOut = File.Create(outFile))
                                {
                                    Stream compStream = new SubStream(dataReader.BaseStream, compLen);
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
                                    if (decStream != compStream)
                                        decStream.Dispose();
                                    compStream.Dispose();
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
                        else if (type == EntryType.Directory)
                        {
                            // skip
                        }
                    }
                }
            }
            Console.WriteLine($"[StorageSphere] Entry '{entry}' not found in archive.");
        }

        // ADD SINGLE FILE
        public void AddFiles(string archive, string[] files)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "storagesphere_add_" + Guid.NewGuid().ToString("N"));
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
                    Console.WriteLine($"[StorageSphere] Added files to archive.");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
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
                if (version < 3)
                    throw new InvalidDataException($"Unsupported archive version: {version}. Version 3+ required for large file support.");
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                byte[] key = null;
                byte[] hmacKey = null;
                if (encrypted)
                {
                    if (!_quiet && !string.IsNullOrWhiteSpace(hint))
                        Console.WriteLine($"Password hint: {hint}");
                    string password = CryptoHelper.PromptPassword("Enter password to list archive: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                    hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    hmacKey = new byte[CryptoHelper.KeySize];
                    Array.Clear(hmacKey, 0, hmacKey.Length);
                }

                fs.Position = bodyStart;
                byte[] dataSection = new byte[dataLen];
                fs.Read(dataSection, 0, (int)dataLen);

                byte[] computedHmac = CryptoHelper.ComputeHmac(hmacKey, new MemoryStream(dataSection));
                fs.Position = hmacPos;
                byte[] fileHmac = reader.ReadBytes(CryptoHelper.HmacSize);
                if (!CryptoHelper.VerifyHmac(hmacKey, new MemoryStream(dataSection), fileHmac))
                {
                    Console.WriteLine("[StorageSphere] ERROR: Archive integrity check failed (HMAC mismatch)!");
                    return;
                }

                Stream dataStream = new MemoryStream(dataSection, 0, dataSection.Length);
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
                        if (Path.IsPathRooted(rel) || rel.Contains(".."))
                            throw new InvalidDataException("Invalid path in archive: " + rel);

                        var meta = FileMetadata.Read(dataReader);
                        string typeStr = type == EntryType.Directory ? "[DIR ]" : "[FILE]";
                        string perms = meta.UnixPerms ?? "";
                        long origLen = 0;
                        long compLen = 0;
                        if (type == EntryType.File)
                        {
                            origLen = dataReader.ReadInt64();
                            compLen = dataReader.ReadInt64();
                            dataReader.BaseStream.Seek(compLen, SeekOrigin.Current);
                        }
                        Console.WriteLine($"{typeStr} {rel} | perms: {perms}");
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
                if (version < 3)
                    throw new InvalidDataException($"Unsupported archive version: {version}. Version 3+ required for large file support.");
                CompressionType compType = (CompressionType)reader.ReadByte();
                bool encrypted = reader.ReadBoolean();
                string hint = reader.ReadString();
                byte[] salt = reader.ReadBytes(CryptoHelper.SaltSize);
                byte[] iv = reader.ReadBytes(CryptoHelper.IvSize);

                long bodyStart = fs.Position;
                long hmacPos = fs.Length - CryptoHelper.HmacSize;
                long dataLen = hmacPos - bodyStart;

                long fileCount = 0, dirCount = 0;
                long origTotal = 0, compTotal = 0;
                byte[] key = null;
                byte[] hmacKey = null;
                if (encrypted)
                {
                    string password = CryptoHelper.PromptPassword("Enter password to show info: ");
                    key = CryptoHelper.DeriveKey(password, salt);
                    hmacKey = CryptoHelper.DeriveKey(password + "HMAC", salt);
                }
                else
                {
                    key = new byte[CryptoHelper.KeySize];
                    hmacKey = new byte[CryptoHelper.KeySize];
                    Array.Clear(hmacKey, 0, hmacKey.Length);
                }

                fs.Position = bodyStart;
                byte[] dataSection = new byte[dataLen];
                fs.Read(dataSection, 0, (int)dataLen);

                byte[] computedHmac = CryptoHelper.ComputeHmac(hmacKey, new MemoryStream(dataSection));
                fs.Position = hmacPos;
                byte[] fileHmac = reader.ReadBytes(CryptoHelper.HmacSize);
                if (!CryptoHelper.VerifyHmac(hmacKey, new MemoryStream(dataSection), fileHmac))
                {
                    Console.WriteLine("[StorageSphere] ERROR: Archive integrity check failed (HMAC mismatch)!");
                    return;
                }

                Stream dataStream = new MemoryStream(dataSection, 0, dataSection.Length);
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
                        if (Path.IsPathRooted(rel) || rel.Contains(".."))
                            throw new InvalidDataException("Invalid path in archive: " + rel);

                        var meta = FileMetadata.Read(dataReader);
                        if (type == EntryType.File)
                        {
                            fileCount++;
                            long origLen = dataReader.ReadInt64();
                            long compLen = dataReader.ReadInt64();
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
                Console.WriteLine("======================================");
            }
        }

        // CHANGE PASSWORD
        public void ChangePassword(string archive)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "storagesphere_passwd_" + Guid.NewGuid().ToString("N"));
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

        private string GetSafeRelativePath(string root, string full)
        {
            var rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(Path.GetFullPath(full));
            var rel = Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(rel) || rel == "." || rel == string.Empty)
                rel = Path.GetFileName(root);
            return rel;
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

        // Helper for streaming only a segment of a stream (used for decompress)
        private class SubStream : Stream
        {
            private readonly Stream _base;
            private long _remain;
            public SubStream(Stream baseStream, long length)
            {
                _base = baseStream;
                _remain = length;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _remain;
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remain <= 0) return 0;
                if (count > _remain) count = (int)_remain;
                int n = _base.Read(buffer, offset, count);
                _remain -= n;
                return n;
            }
            public override int Read(Span<byte> buffer)
            {
                if (_remain <= 0) return 0;
                int count = buffer.Length;
                if (count > _remain) count = (int)_remain;
                int n = _base.Read(buffer.Slice(0, count));
                _remain -= n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}