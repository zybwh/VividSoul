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

        public string GetManifestPath(string itemId) => NormalizePath(Path.Combine(GetItemDirectory(itemId), ManifestFileName));

        public string GetModelPath(string itemId) => NormalizePath(Path.Combine(GetItemDirectory(itemId), ModelFileName));

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
    }
}
