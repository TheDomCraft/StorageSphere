using System;
using System.IO;
using System.Collections;
using System.Security.Cryptography;
using System.Text;

namespace StorageSphere
{
    public static class CryptoHelper
    {
        public const int SaltSize = 16;
        public const int KeySize = 32;
        public const int IvSize = 16;
        public const int HmacSize = 32;
        public const int PBKDF2_Iterations = 100_000;

        public static string PromptPassword(string prompt)
        {
            Console.Write(prompt);
            StringBuilder sb = new StringBuilder();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write("*");
                }
            } while (true);
            Console.WriteLine();
            return sb.ToString();
        }

        public static byte[] DeriveKey(string password, byte[] salt, int size = KeySize)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, PBKDF2_Iterations, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(size);
            }
        }

        public static byte[] ComputeHmac(byte[] key, Stream stream)
        {
            using (var hmac = new HMACSHA256(key))
            {
                long origPos = stream.Position;
                stream.Position = 0;
                byte[] hash = hmac.ComputeHash(stream);
                stream.Position = origPos;
                return hash;
            }
        }

        public static bool VerifyHmac(byte[] key, Stream stream, byte[] expected)
        {
            byte[] actual = ComputeHmac(key, stream);
            return StructuralComparisons.StructuralEqualityComparer.Equals(actual, expected);
        }
    }
}