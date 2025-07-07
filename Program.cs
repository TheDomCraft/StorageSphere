using System;

namespace StorageSphere
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                PrintUsage();
                return;
            }

            var cli = new CLI(args);
            cli.Run();
        }

        public static void PrintUsage()
        {
            Console.WriteLine(@"============================================================================
StorageSphere Archive | Copyright (C) 2025 TheDomCraft
============================================================================
Usage: storagesphere <command> [options]

Commands:
  pack <archive> [items...]         Create or replace archive
  unpack <archive> <outdir>         Extract all archive contents
  extract <archive> <entry> <out>   Extract a single file
  add <archive> [items...]          Add files/dirs to archive
  list <archive>                    List archive contents
  info <archive>                    Show archive info and sizes
  passwd <archive>                  Change archive password
Options:
  -p, --set-password                Encrypt archive with password
  -h, --hint <hint>                 Archive password hint
  -c, --compression <mode>          Compression: deflate, gzip, brotli, none
  -v, --verbose                     Verbose output
  -q, --quiet                       Quiet mode
============================================================================");
        }
    }
}