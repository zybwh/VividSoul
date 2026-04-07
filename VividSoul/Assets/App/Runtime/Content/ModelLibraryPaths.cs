#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace VividSoul.Runtime.Content
{
    public sealed class ModelLibraryPaths
    {
        private const string ContentDirectoryName = "Content";
        private const string ModelsDirectoryName = "Models";
        private const string ManifestFileName = "item.json";
        private const string ModelFileName = "model.vrm";
        private const int DirectoryHashPrefixLength = 12;
        private const int MaxDirectorySlugLength = 24;

        private readonly string rootPath;

        public ModelLibraryPaths(string? baseDirectory = null)
        {
            var resolvedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            rootPath = NormalizePath(Path.Combine(
                resolvedBaseDirectory,
                ContentDirectoryName,
                ModelsDirectoryName));
        }

        public string RootPath => rootPath;

        public string EnsureRootDirectory()
        {
            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        public string GetItemDirectory(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException("A model library item id is required.", nameof(itemId));
            }

            return NormalizePath(Path.Combine(rootPath, itemId.Trim()));
        }

        public string GetPreferredItemDirectory(string itemId, string title)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException("A model library item id is required.", nameof(itemId));
            }

            return NormalizePath(Path.Combine(rootPath, BuildPreferredDirectoryName(itemId.Trim(), title)));
        }

        public string GetManifestPath(string itemId) => NormalizePath(Path.Combine(GetItemDirectory(itemId), ManifestFileName));

        public string GetModelPath(string itemId) => NormalizePath(Path.Combine(GetItemDirectory(itemId), ModelFileName));

        public string GetManifestPathForDirectory(string itemDirectory) => NormalizePath(Path.Combine(itemDirectory, ManifestFileName));

        public string GetModelPathForDirectory(string itemDirectory) => NormalizePath(Path.Combine(itemDirectory, ModelFileName));

        public string GetDisplayRelativeItemDirectory(string itemDirectory)
        {
            if (string.IsNullOrWhiteSpace(itemDirectory))
            {
                throw new ArgumentException("A model library item directory is required.", nameof(itemDirectory));
            }

            var normalizedDirectory = NormalizePath(itemDirectory).TrimEnd('/');
            var directoryName = Path.GetFileName(normalizedDirectory);
            var displayName = GetDisplayDirectoryName(directoryName);
            return $"{ContentDirectoryName}/{ModelsDirectoryName}/{displayName}";
        }

        public bool ContainsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalizedPath = NormalizePath(path);
            if (string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedPath.StartsWith($"{rootPath}/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/');
        }

        private static string BuildPreferredDirectoryName(string itemId, string title)
        {
            var slug = Slugify(title);
            var shortHash = itemId.Length <= DirectoryHashPrefixLength
                ? itemId
                : itemId.Substring(0, DirectoryHashPrefixLength);
            return $"{slug}-{shortHash}";
        }

        private static string GetDisplayDirectoryName(string directoryName)
        {
            if (!LooksLikeLongHash(directoryName))
            {
                return directoryName;
            }

            var shortHash = directoryName.Length <= DirectoryHashPrefixLength
                ? directoryName
                : directoryName.Substring(0, DirectoryHashPrefixLength);
            return $"{shortHash}...";
        }

        private static bool LooksLikeLongHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 32)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Slugify(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "model";
            }

            var buffer = new char[Math.Min(title.Length, MaxDirectorySlugLength * 2)];
            var count = 0;
            var previousWasSeparator = false;
            foreach (var character in title.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    if (count >= MaxDirectorySlugLength)
                    {
                        break;
                    }

                    buffer[count++] = char.ToLowerInvariant(character);
                    previousWasSeparator = false;
                    continue;
                }

                if (previousWasSeparator || count == 0)
                {
                    continue;
                }

                if (count >= MaxDirectorySlugLength)
                {
                    break;
                }

                buffer[count++] = '-';
                previousWasSeparator = true;
            }

            while (count > 0 && buffer[count - 1] == '-')
            {
                count--;
            }

            return count == 0
                ? "model"
                : new string(buffer, 0, count);
        }
    }
}
