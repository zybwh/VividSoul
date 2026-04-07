#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VividSoul.Runtime.AI;

namespace VividSoul.Runtime.Content
{
    public static class VrmMetadataNameProbe
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunkType = 0x4E4F534A;

        public static bool TryReadDisplayName(string path, out string displayName)
        {
            displayName = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                var json = TryReadJsonChunk(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                if (MiniJson.Deserialize(json) is not Dictionary<string, object?> root)
                {
                    return false;
                }

                displayName = ResolveName(root);
                return !string.IsNullOrWhiteSpace(displayName);
            }
            catch
            {
                displayName = string.Empty;
                return false;
            }
        }

        private static string TryReadJsonChunk(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (reader.ReadUInt32() != GlbMagic)
            {
                return string.Empty;
            }

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkLength = reader.ReadUInt32();
                var chunkType = reader.ReadUInt32();
                var chunkBytes = reader.ReadBytes((int)chunkLength);
                if (chunkType == JsonChunkType)
                {
                    return Encoding.UTF8.GetString(chunkBytes).TrimEnd('\0', ' ', '\t', '\r', '\n');
                }
            }

            return string.Empty;
        }

        private static string ResolveName(Dictionary<string, object?> root)
        {
            if (!TryGetObject(root, "extensions", out var extensions))
            {
                return string.Empty;
            }

            if (TryGetObject(extensions, "VRMC_vrm", out var vrm1)
                && TryGetObject(vrm1, "meta", out var vrm1Meta))
            {
                var vrm1Name = FirstNonEmptyString(vrm1Meta, "name", "Name", "title", "Title");
                if (!string.IsNullOrWhiteSpace(vrm1Name))
                {
                    return vrm1Name;
                }
            }

            if (TryGetObject(extensions, "VRM", out var vrm0)
                && TryGetObject(vrm0, "meta", out var vrm0Meta))
            {
                var vrm0Name = FirstNonEmptyString(vrm0Meta, "title", "Title", "name", "Name");
                if (!string.IsNullOrWhiteSpace(vrm0Name))
                {
                    return vrm0Name;
                }
            }

            return string.Empty;
        }

        private static bool TryGetObject(Dictionary<string, object?> source, string key, out Dictionary<string, object?> value)
        {
            if (source.TryGetValue(key, out var result) && result is Dictionary<string, object?> dictionary)
            {
                value = dictionary;
                return true;
            }

            value = null!;
            return false;
        }

        private static string FirstNonEmptyString(Dictionary<string, object?> source, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (source.TryGetValue(key, out var value)
                    && value is string stringValue
                    && !string.IsNullOrWhiteSpace(stringValue))
                {
                    return stringValue.Trim();
                }
            }

            return string.Empty;
        }
    }
}
