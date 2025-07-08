using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StorageSphere
{
    public static class Utils
    {
        public static string GetUnixPermissions(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var fileInfo = new Mono.Unix.UnixFileInfo(path);
                    return Convert.ToString((int)fileInfo.FileAccessPermissions, 8);
                }
                catch
                {
                    return "";
                }
            }
            return "";
        }

        public static void SetUnixPermissions(string path, string perms)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrEmpty(perms))
            {
                try
                {
                    var fileInfo = new Mono.Unix.UnixFileInfo(path);
                    fileInfo.FileAccessPermissions = (Mono.Unix.FileAccessPermissions)Convert.ToInt32(perms, 8);
                }
                catch { }
            }
        }

        // We dont support Symlinks at this time, so this is basicly useless right now...
        public static bool IsSymlink(FileSystemInfo fi) => false;
        public static string ReadSymlink(string path) => "";
        public static void CreateSymlink(string target, string link) { }

        public static string HumanSize(long size)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = size;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}