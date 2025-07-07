# StorageSphere

**StorageSphere** is a robust, cross-platform, modern archiver utility and archive format. This is a proof-of-concept project created by me. It is designed for learning, experimentation, and as a foundation for more advanced archiving tools.  
**This project is not production-ready. Use at your own risk.**

---

## ✨ Features

- **Cross-platform:** Works on Windows, Linux, and macOS (.NET 6+ required; full metadata support on Unix-like systems with [Mono.Posix.NETStandard](https://www.nuget.org/packages/Mono.Posix.NETStandard)).
- **Optional AES-256 Encryption:** Secure your archives with a password and optional password hint.
- **HMAC-SHA256 Integrity:** Detects tampering or corruption with cryptographic checks.
- **Flexible Compression:** Choose between Deflate, GZip, Brotli, or no compression.
- **Rich Metadata Support:**
    - File/directory attributes and timestamps
    - Unix permissions (rwx bits) where available
    - (No symlink support in this version)
- **Password Change Utility:** Change the archive password without re-archiving your files.
- **Per-file Extraction/Add:** Extract or add individual files without full unpack.
- **Archive Info/Stats:** View summary and size details easily.
- **Progress Bar:** Visual progress for large operations.
- **Verbose/Quiet Modes:** Control output detail as needed.
- **Automatic File Extension:** `.ssph` is appended if omitted on packing.

---

## 🚧 Limitations / Status

- **Proof of Concept:** Not intended for production or long-term archival.
- **No GUI:** CLI only.
- **No advanced safety features:** (e.g. dry-run, file overwrite prompts, archive splitting)
- **No multi-threading:** Compression is single-threaded.
- **No incremental updates:** Adding/removing files repacks the archive.
- **No symlink support:** (deliberately removed for maximum reliability)

---

## 🛠️ Requirements

- [.NET 6+ SDK or newer](https://dotnet.microsoft.com/download)
- [Mono.Posix.NETStandard](https://www.nuget.org/packages/Mono.Posix.NETStandard) (for full Unix support)

Install dependencies with:

```sh
dotnet add package Mono.Posix.NETStandard
```

---

## 📦 Build

```sh
dotnet build
```

---

## 🚀 Usage

```
storagesphere <command> [options]
```

### Commands

| Command | Description |
| ------- | ----------- |
| `pack <archive> [items...]`   | Create or replace an archive from files/directories |
| `unpack <archive> <outdir>`   | Extract all contents |
| `extract <archive> <entry> <out>` | Extract a single file |
| `add <archive> [items...]`    | Add files/dirs to archive (repack) |
| `list <archive>`              | List archive contents |
| `info <archive>`              | Show archive info and stats |
| `passwd <archive>`            | Change the archive password |

### Options

| Option | Description |
| ------ | ----------- |
| `-p`, `--set-password`  | Encrypt archive with password |
| `-h`, `--hint <hint>`   | Store a password hint (unencrypted) |
| `-c`, `--compression <mode>` | Compression: `deflate`, `gzip`, `brotli`, `none` (default: `deflate`) |
| `-v`, `--verbose`       | Verbose output |
| `-q`, `--quiet`         | Quiet mode (minimal output) |

#### Example: Create a password-protected archive

```sh
storagesphere pack mybackup folder1 file2.txt -p --hint "It's a song"
```

#### Example: Extract a single file

```sh
storagesphere extract mybackup.ssph docs/readme.md ./restored-readme.md
```

#### Example: Show archive info

```sh
storagesphere info mybackup.ssph
```

---

## 📝 Archive Format

- **Header:** Magic (`SSPH`), version, compression, encryption flag, password hint, salt, IV.
- **Entries:** Each file/dir with type, path, metadata, and (for files) compressed/encrypted content.
- **HMAC:** At the end, an HMAC-SHA256 value to verify archive integrity.
- **Password:** If encrypted, key is derived via PBKDF2 using salt and password. HMAC key is derived from password as well.

---

## 🔒 Security

- If `-p` is used, all file data and metadata is encrypted with AES-256-CBC.
- The HMAC provides strong tamper detection—archives with a wrong password or corrupted data will refuse to extract.
- Password hint is stored in plaintext and is only a user-friendly comment—do NOT put secrets in the hint!

---

## 🐧 Platform Notes

- **Unix Permissions:** On Linux/macOS, permissions are handled using Mono.Posix. On Windows, only basic file attributes are restored.
- **Cross-Platform:** Archives created on one OS can be extracted on another, but some metadata may not round-trip perfectly.

---

## 📚 Further Improvements & Roadmap

This proof of concept demonstrates a flexible, extensible, and robust archive format for modern use.  
Ideas for the future:
- Incremental updates and removal
- Multithreaded compression
- Archive splitting/joining
- GUI front-end
- Portable single-file binaries

---

## 👤 Author

Created by **TheDomCraft**  
Want to contribute or fork? Go ahead!  
Suggestions, bug reports, and feature ideas are welcome.

---

## 📖 License

MIT License (see [LICENSE](LICENSE) file).

---

**Enjoy experimenting with StorageSphere!**