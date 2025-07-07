using System;
using System.Linq;

namespace StorageSphere
{
    public class CLI
    {
        private readonly string[] _args;

        public CLI(string[] args)
        {
            _args = args;
        }

        public void Run()
        {
            if (_args.Length < 1)
            {
                Program.PrintUsage();
                return;
            }

            string cmd = _args[0].ToLowerInvariant();
            string[] rest = _args.Skip(1).ToArray();

            // Global options
            bool verbose = rest.Contains("-v") || rest.Contains("--verbose");
            bool quiet = rest.Contains("-q") || rest.Contains("--quiet");
            string compression = ParseOption(rest, "-c", "--compression") ?? "deflate";
            bool setPassword = rest.Contains("-p") || rest.Contains("--set-password");
            string hint = ParseOption(rest, "-h", "--hint");

            var manager = new ArchiveManager(verbose, quiet);

            switch (cmd)
            {
                case "pack":
                    {
                        string archive = rest.Length > 0 ? rest[0] : null;
                        if (string.IsNullOrEmpty(archive)) { Program.PrintUsage(); return; }
                        string[] items = rest.Skip(1).Where(s => !s.StartsWith("-")).ToArray();
                        if (!archive.EndsWith(".ssph", StringComparison.OrdinalIgnoreCase))
                            archive += ".ssph";
                        manager.Pack(archive, items, setPassword, hint, compression);
                        break;
                    }
                case "unpack":
                    {
                        if (rest.Length < 2) { Program.PrintUsage(); return; }
                        manager.Unpack(rest[0], rest[1]);
                        break;
                    }
                case "extract":
                    {
                        if (rest.Length < 3) { Program.PrintUsage(); return; }
                        manager.ExtractSingle(rest[0], rest[1], rest[2]);
                        break;
                    }
                case "add":
                    {
                        if (rest.Length < 2) { Program.PrintUsage(); return; }
                        string archive = rest[0];
                        string[] items = rest.Skip(1).ToArray();
                        manager.AddFiles(archive, items);
                        break;
                    }
                case "list":
                    {
                        if (rest.Length < 1) { Program.PrintUsage(); return; }
                        manager.ListContents(rest[0]);
                        break;
                    }
                case "info":
                    {
                        if (rest.Length < 1) { Program.PrintUsage(); return; }
                        manager.ShowInfo(rest[0]);
                        break;
                    }
                case "passwd":
                    {
                        if (rest.Length < 1) { Program.PrintUsage(); return; }
                        manager.ChangePassword(rest[0]);
                        break;
                    }
                default:
                    Program.PrintUsage();
                    break;
            }
        }

        private string ParseOption(string[] args, string shortOpt, string longOpt)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == shortOpt || args[i] == longOpt)
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        return args[i + 1];
                }
            }
            return null;
        }
    }
}