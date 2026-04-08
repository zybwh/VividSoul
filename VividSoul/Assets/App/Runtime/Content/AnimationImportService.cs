#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace VividSoul.Runtime.Content
{
    public sealed class AnimationImportService
    {
        private readonly AnimationLibraryPaths libraryPaths;
        private readonly FileSystemContentCatalog contentCatalog;
        private readonly ModelFingerprintService fingerprintService;

        public AnimationImportService(
            AnimationLibraryPaths libraryPaths,
            FileSystemContentCatalog contentCatalog,
            ModelFingerprintService fingerprintService)
        {
            this.libraryPaths = libraryPaths ?? throw new ArgumentNullException(nameof(libraryPaths));
            this.contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
            this.fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        }

        public AnimationImportResult ImportFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("An animation file path is required.", nameof(sourcePath));
            }

            var normalizedSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(normalizedSourcePath))
            {
                throw new FileNotFoundException("The animation file does not exist.", normalizedSourcePath);
            }

            if (!string.Equals(Path.GetExtension(normalizedSourcePath), ".vrma", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only .vrma files can be imported into the managed action library.");
            }

            var title = Path.GetFileNameWithoutExtension(normalizedSourcePath);
            var itemId = fingerprintService.ComputeSha256(normalizedSourcePath);
            var itemDirectory = libraryPaths.GetPreferredItemDirectory(itemId, title);
            var manifestPath = libraryPaths.GetManifestPathForDirectory(itemDirectory);
            var targetAnimationPath = libraryPaths.GetAnimationPathForDirectory(itemDirectory);
            var importedNewItem = !File.Exists(manifestPath) || !File.Exists(targetAnimationPath);

            libraryPaths.EnsureRootDirectory();
            Directory.CreateDirectory(itemDirectory);

            if (importedNewItem)
            {
                File.Copy(normalizedSourcePath, targetAnimationPath, overwrite: true);
                File.WriteAllText(
                    manifestPath,
                    JsonUtility.ToJson(
                        new ContentManifestFile
                        {
                            schemaVersion = 1,
                            type = ContentType.Animation.ToString(),
                            title = title,
                            entry = Path.GetFileName(targetAnimationPath),
                            ageRating = "Everyone",
                            tags = Array.Empty<string>(),
                        },
                        prettyPrint: true));
            }

            if (!contentCatalog.TryCreateItem(itemDirectory, ContentSource.Local, out var item))
            {
                throw new InvalidOperationException($"Failed to index imported animation library item: {itemDirectory}");
            }

            return new AnimationImportResult(item, importedNewItem);
        }
    }

    public sealed record AnimationImportResult(ContentItem Item, bool ImportedNewItem);
}
