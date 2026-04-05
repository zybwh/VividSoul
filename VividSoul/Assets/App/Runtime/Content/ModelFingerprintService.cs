#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;

namespace VividSoul.Runtime.Content
{
    public sealed class ModelFingerprintService
    {
        public string ComputeSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("The model file does not exist.", normalizedPath);
            }

            using var stream = File.OpenRead(normalizedPath);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return $"sha256:{ToLowerHex(hash)}";
        }

        private static string ToLowerHex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var chars = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                chars[index * 2] = ToLowerHexChar(value >> 4);
                chars[(index * 2) + 1] = ToLowerHexChar(value & 0xF);
            }

            return new string(chars);
        }

        private static char ToLowerHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }
    }
}
