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
using System.IO.Compression;

class StorSphere
{
    static void Main(string[] args)
    {
        if (args.Length < 3 || (args[0] != "pack" && args[0] != "unpack" && args[0] != "list"))
        {
            Console.WriteLine("=====================================================================");
            Console.WriteLine("StorSphere Archive | Copyright (C) 2020 - 2024 CraftMusic App Studios");
            Console.WriteLine("=====================================================================");
            Console.WriteLine("Usage: storsphere pack <output.ssph> <file1> <file2> ...");
            Console.WriteLine("Usage: storsphere unpack <input.ssph> <output_folder>");
            Console.WriteLine("Usage: storsphere list <input.ssph>");
            Console.WriteLine("=====================================================================");
            return;
        }

        string command = args[0];
        string archiveName = args[1];

        if (command == "pack")
        {
            string[] filesToPack = new string[args.Length - 2];
            Array.Copy(args, 2, filesToPack, 0, args.Length - 2);
            Pack(archiveName, filesToPack);
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
    }

    static void Pack(string archiveName, string[] filesToPack)
    {
        using (FileStream fsOut = File.Create(archiveName))
        {
            using (GZipStream gzStream = new GZipStream(fsOut, CompressionMode.Compress))
            {
                using (BinaryWriter writer = new BinaryWriter(gzStream))
                {
                    foreach (string file in filesToPack)
                    {
                        byte[] fileBytes = File.ReadAllBytes(file);
                        writer.Write(fileBytes.Length);
                        writer.Write(Path.GetFileName(file));
                        writer.Write(fileBytes);
                    }
                }
            }
        }
        Console.WriteLine($"[StorSphere] Packed files into {archiveName}");
    }

    static void Unpack(string archiveName, string outputFolder)
    {
        using (FileStream fsIn = File.OpenRead(archiveName))
        {
            using (GZipStream gzStream = new GZipStream(fsIn, CompressionMode.Decompress))
            {
                using (BinaryReader reader = new BinaryReader(gzStream))
                {
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        int fileSize = reader.ReadInt32();
                        string fileName = reader.ReadString();
                        byte[] fileData = reader.ReadBytes(fileSize);

                        string filePath = Path.Combine(outputFolder, fileName);
                        File.WriteAllBytes(filePath, fileData);
                    }
                }
            }
        }
        Console.WriteLine($"[StorSphere] Unpacked {archiveName} to {outputFolder}");
    }

    static void ListContents(string archiveName)
    {
        using (FileStream fsIn = File.OpenRead(archiveName))
        {
            using (GZipStream gzStream = new GZipStream(fsIn, CompressionMode.Decompress))
            {
                using (BinaryReader reader = new BinaryReader(gzStream))
                {
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        int fileSize = reader.ReadInt32();
                        string fileName = reader.ReadString();
                        Console.WriteLine($"{fileName} - {fileSize} bytes");
                        reader.BaseStream.Seek(fileSize, SeekOrigin.Current); // Skip the file content
                    }
                }
            }
        }
    }
}
