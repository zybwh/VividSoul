#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace VividSoul.Runtime.Content
{
    public sealed class ModelImportService
    {
        private const string SupportedModelExtension = ".vrm";

        private readonly FileSystemContentCatalog contentCatalog;
        private readonly ModelFingerprintService fingerprintService;
        private readonly ModelLibraryPaths modelLibraryPaths;

        public ModelImportService(
            ModelLibraryPaths modelLibraryPaths,
            FileSystemContentCatalog contentCatalog,
            ModelFingerprintService fingerprintService)
        {
            this.modelLibraryPaths = modelLibraryPaths ?? throw new ArgumentNullException(nameof(modelLibraryPaths));
            this.contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
            this.fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        }

        public ModelImportResult Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("A model path is required.", nameof(sourcePath));
            }

            var normalizedSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(normalizedSourcePath))
            {
                throw new FileNotFoundException("The model file does not exist.", normalizedSourcePath);
            }

            if (!string.Equals(Path.GetExtension(normalizedSourcePath), SupportedModelExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported model extension: {Path.GetExtension(normalizedSourcePath)}");
            }

            var fingerprint = fingerprintService.ComputeSha256(normalizedSourcePath);
            var itemId = fingerprint.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? fingerprint.Substring("sha256:".Length)
                : fingerprint;
            var title = ResolveTitle(normalizedSourcePath);
            var itemDirectory = ResolveItemDirectory(itemId, title, fingerprint);
            var targetModelPath = modelLibraryPaths.GetModelPathForDirectory(itemDirectory);
            var manifestPath = modelLibraryPaths.GetManifestPathForDirectory(itemDirectory);
            var importedNewItem = false;

            Directory.CreateDirectory(modelLibraryPaths.EnsureRootDirectory());
            Directory.CreateDirectory(itemDirectory);

            if (!File.Exists(targetModelPath))
            {
                File.Copy(normalizedSourcePath, targetModelPath, overwrite: false);
                importedNewItem = true;
            }

            if (!File.Exists(manifestPath))
            {
                WriteManifest(
                    manifestPath,
                    itemId,
                    title,
                    fingerprint);
                importedNewItem = true;
            }

            if (!contentCatalog.TryCreateItem(itemDirectory, ContentSource.Local, out var item))
            {
                throw new InvalidOperationException($"Failed to index imported model library item: {itemDirectory}");
            }

            return new ModelImportResult(item, fingerprint, importedNewItem);
        }

        private string ResolveItemDirectory(string itemId, string title, string fingerprint)
        {
            var existingDirectory = TryFindExistingItemDirectory(itemId, fingerprint);
            return string.IsNullOrWhiteSpace(existingDirectory)
                ? modelLibraryPaths.GetPreferredItemDirectory(itemId, title)
                : existingDirectory;
        }

        private string? TryFindExistingItemDirectory(string itemId, string fingerprint)
        {
            var legacyDirectory = modelLibraryPaths.GetItemDirectory(itemId);
            if (Directory.Exists(legacyDirectory))
            {
                return legacyDirectory;
            }

            var rootDirectory = modelLibraryPaths.EnsureRootDirectory();
            foreach (var directory in Directory.GetDirectories(rootDirectory))
            {
                var manifestPath = modelLibraryPaths.GetManifestPathForDirectory(directory);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var manifest = JsonUtility.FromJson<ModelLibraryManifestFile>(File.ReadAllText(manifestPath));
                    if (manifest == null)
                    {
                        continue;
                    }

                    if (string.Equals(manifest.id, itemId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(manifest.fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFullPath(directory);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string ResolveTitle(string sourcePath)
        {
            if (VrmMetadataNameProbe.TryReadDisplayName(sourcePath, out var displayName))
            {
                return displayName;
            }

            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            return string.IsNullOrWhiteSpace(fileName)
                ? "Imported Model"
                : fileName.Trim();
        }

        private static void WriteManifest(
            string manifestPath,
            string itemId,
            string title,
            string fingerprint)
        {
            var manifestFile = new ModelLibraryManifestFile
            {
                schemaVersion = 2,
                type = "Model",
                id = itemId,
                title = title,
                entry = "model.vrm",
                preview = string.Empty,
                thumbnail = string.Empty,
                source = "Local",
                sourceId = string.Empty,
                author = string.Empty,
                description = string.Empty,
                ageRating = "Unknown",
                importedAt = DateTimeOffset.UtcNow.ToString("O"),
                fingerprint = fingerprint,
                tags = new[] { "vrm" },
            };

            var json = JsonUtility.ToJson(manifestFile, true);
            File.WriteAllText(manifestPath, json);
        }

        [Serializable]
        private sealed class ModelLibraryManifestFile
        {
            public int schemaVersion = 2;
            public string type = "Model";
            public string id = string.Empty;
            public string title = string.Empty;
            public string entry = "model.vrm";
            public string preview = string.Empty;
            public string thumbnail = string.Empty;
            public string source = "Local";
            public string sourceId = string.Empty;
            public string author = string.Empty;
            public string description = string.Empty;
            public string ageRating = "Unknown";
            public string[] tags = Array.Empty<string>();
            public string importedAt = string.Empty;
            public string fingerprint = string.Empty;
        }
    }
}
