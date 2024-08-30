/*---------------------------------------------------------------------------*
   Project:  StorSphere
   File:     Program.cs
 
   Copyright (C) 2020-2024 CraftMusic App Studios. All rights reserved.
 
   These coded instructions, statements, and computer programs contain
   proprietary information of the CraftMusic App Studios, and are
   protected by Federal copyright law. They may not be disclosed to
   third parties or copied or duplicated in any form, in whole or in part,
   without the prior written consent of the CraftMusic App Studios.
 *---------------------------------------------------------------------------*/

using System;
using System.IO;

class StorSphere
{
    static void Main(string[] args)
    {
        if (args.Length < 2 || (
            args[0] != "pack" && 
            args[0] != "unpack" && 
            args[0] != "list" && 
            args[0] != "info"))
        {
            Console.WriteLine("============================================================================");
            Console.WriteLine("StorSphere Archive | Copyright (C) 2020 - 2024 CraftMusic App Studios");
            Console.WriteLine("============================================================================");
            Console.WriteLine("Usage: storsphere pack <output.ssph> <file_or_folder1> <file_or_folder2> ...");
            Console.WriteLine("Usage: storsphere unpack <input.ssph> <output_folder>");
            Console.WriteLine("Usage: storsphere list <input.ssph>");
            Console.WriteLine("============================================================================");
            return;
        }

        string command = args[0];
        string archiveName = args[1];

        if (command == "pack")
        {
            string[] itemsToPack = new string[args.Length - 2];
            Array.Copy(args, 2, itemsToPack, 0, args.Length - 2);
            Pack(archiveName, itemsToPack);
        }
        else if (command == "unpack")
        {
            string outputFolder = args[2];
            Unpack(archiveName, outputFolder);
        }
        else if (command == "list")
        {
            ListContents(archiveName);
        }
        else if (command == "info")
        {
            Console.WriteLine("=====================================================================");
            Console.WriteLine("StorSphere Archive | Copyright (C) 2020 - 2024 CraftMusic App Studios");
            Console.WriteLine("=====================================================================");
            Console.WriteLine("StorSphere Version: 1.2.0.0");
            Console.WriteLine("StorSphere Build: 300820240550");
            Console.WriteLine("=====================================================================");
        }
    }

    static void Pack(string archiveName, string[] itemsToPack)
    {
        using (FileStream fsOut = File.Create(archiveName))
        {
            using (BinaryWriter writer = new BinaryWriter(fsOut))
            {
                foreach (string item in itemsToPack)
                {
                    if (Directory.Exists(item))
                    {
                        PackDirectory(writer, item, item);
                    }
                    else if (File.Exists(item))
                    {
                        PackFile(writer, item, Path.GetFileName(item));
                    }
                }
            }
        }
        Console.WriteLine($"[StorSphere] Packed items into {archiveName}");
    }

    static void PackDirectory(BinaryWriter writer, string rootPath, string currentPath)
    {
        foreach (string directory in Directory.GetDirectories(currentPath))
        {
            PackDirectory(writer, rootPath, directory);
        }

        foreach (string file in Directory.GetFiles(currentPath))
        {
            string relativePath = Path.GetRelativePath(rootPath, file);
            PackFile(writer, file, relativePath);
        }
    }

    static void PackFile(BinaryWriter writer, string filePath, string entryName)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        writer.Write(fileBytes.Length);
        writer.Write(entryName);
        writer.Write(fileBytes);
    }

    static void Unpack(string archiveName, string outputFolder)
    {
        using (FileStream fsIn = File.OpenRead(archiveName))
        {
            using (BinaryReader reader = new BinaryReader(fsIn))
            {
                try
                {
                    while (true)
                    {
                        int fileSize = reader.ReadInt32();
                        string fileName = reader.ReadString();
                        byte[] fileData = reader.ReadBytes(fileSize);

                        string filePath = Path.Combine(outputFolder, fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
                        File.WriteAllBytes(filePath, fileData);
                    }
                }
                catch (EndOfStreamException)
                {
                    // Reached the end of the archive
                }
            }
        }
        Console.WriteLine($"[StorSphere] Unpacked {archiveName} to {outputFolder}");
    }

    static void ListContents(string archiveName)
    {
        using (FileStream fsIn = File.OpenRead(archiveName))
        {
            using (BinaryReader reader = new BinaryReader(fsIn))
            {
                try
                {
                    while (true)
                    {
                        int fileSize = reader.ReadInt32();
                        string fileName = reader.ReadString();
                        Console.WriteLine($"{fileName} - {fileSize} bytes");

                        // Manually read and discard the file content to skip it
                        reader.ReadBytes(fileSize);
                    }
                }
                catch (EndOfStreamException)
                {
                    // Reached the end of the archive
                }
            }
        }
    }
}
