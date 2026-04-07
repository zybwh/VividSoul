#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using VividSoul.Runtime.Settings;

namespace VividSoul.Runtime.Content
{
    public sealed class ModelLibraryMigrationService
    {
        private readonly ModelImportService modelImportService;
        private readonly ModelLibraryPaths modelLibraryPaths;
        private readonly IDesktopPetSettingsStore settingsStore;

        public ModelLibraryMigrationService(
            IDesktopPetSettingsStore settingsStore,
            ModelLibraryPaths modelLibraryPaths,
            ModelImportService modelImportService)
        {
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            this.modelLibraryPaths = modelLibraryPaths ?? throw new ArgumentNullException(nameof(modelLibraryPaths));
            this.modelImportService = modelImportService ?? throw new ArgumentNullException(nameof(modelImportService));
        }

        public void MigrateSelectedLocalModelIfNeeded()
        {
            var settings = settingsStore.Load();
            var selectedContent = settings.SelectedContent;
            if (selectedContent == null
                || selectedContent.Source != SelectedContentSource.Local
                || selectedContent.Type != ContentType.Model
                || string.IsNullOrWhiteSpace(selectedContent.Data))
            {
                return;
            }

            var normalizedPath = Path.GetFullPath(selectedContent.Data);
            if (modelLibraryPaths.ContainsPath(normalizedPath)
                || !File.Exists(normalizedPath)
                || !string.Equals(Path.GetExtension(normalizedPath), ".vrm", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importResult = modelImportService.Import(normalizedPath);
            var migratedSelectedContent = selectedContent with { Data = importResult.Item.EntryPath };
            settingsStore.Save(settings with { SelectedContent = migratedSelectedContent });
        }

        public void MigrateManagedLocalModelDirectoriesIfNeeded()
        {
            var rootDirectory = modelLibraryPaths.EnsureRootDirectory();
            if (!Directory.Exists(rootDirectory))
            {
                return;
            }

            var settings = settingsStore.Load();
            var selectedContent = settings.SelectedContent;
            var cachedModels = settings.CachedModels;
            var changed = false;

            foreach (var currentDirectory in Directory.GetDirectories(rootDirectory))
            {
                var manifestPath = modelLibraryPaths.GetManifestPathForDirectory(currentDirectory);
                var modelPath = modelLibraryPaths.GetModelPathForDirectory(currentDirectory);
                if (!File.Exists(manifestPath) || !File.Exists(modelPath))
                {
                    continue;
                }

                ModelLibraryManifestFile? manifest;
                try
                {
                    manifest = JsonUtility.FromJson<ModelLibraryManifestFile>(File.ReadAllText(manifestPath));
                }
                catch
                {
                    continue;
                }

                if (manifest == null
                    || string.IsNullOrWhiteSpace(manifest.id)
                    || string.IsNullOrWhiteSpace(manifest.title)
                    || !string.Equals(manifest.source, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var preferredTitle = cachedModels
                    .FirstOrDefault(model => string.Equals(model.Path, modelPath, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName;
                var normalized = NormalizeManagedLocalModelPathInternal(
                    modelPath,
                    string.IsNullOrWhiteSpace(preferredTitle) ? manifest.title : preferredTitle,
                    selectedContent,
                    cachedModels);
                if (!normalized.Changed)
                {
                    continue;
                }

                selectedContent = normalized.SelectedContent;
                cachedModels = normalized.CachedModels;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            settingsStore.Save(settings with
            {
                SelectedContent = selectedContent,
                CachedModels = cachedModels,
            });
        }

        public string NormalizeManagedLocalModelPath(string modelPath, string preferredDisplayName)
        {
            var settings = settingsStore.Load();
            var normalized = NormalizeManagedLocalModelPathInternal(
                modelPath,
                preferredDisplayName,
                settings.SelectedContent,
                settings.CachedModels);
            if (normalized.Changed)
            {
                settingsStore.Save(settings with
                {
                    SelectedContent = normalized.SelectedContent,
                    CachedModels = normalized.CachedModels,
                });
            }

            return normalized.NormalizedModelPath;
        }

        private NormalizedManagedModelPathResult NormalizeManagedLocalModelPathInternal(
            string modelPath,
            string preferredDisplayName,
            SelectedContentState? selectedContent,
            System.Collections.Generic.IReadOnlyList<CachedModelState> cachedModels)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return new NormalizedManagedModelPathResult(modelPath, selectedContent, cachedModels, false);
            }

            var normalizedModelPath = Path.GetFullPath(modelPath);
            var currentDirectory = Path.GetDirectoryName(normalizedModelPath);
            if (string.IsNullOrWhiteSpace(currentDirectory) || !modelLibraryPaths.ContainsPath(currentDirectory))
            {
                return new NormalizedManagedModelPathResult(normalizedModelPath, selectedContent, cachedModels, false);
            }

            var manifestPath = modelLibraryPaths.GetManifestPathForDirectory(currentDirectory);
            if (!File.Exists(manifestPath))
            {
                return new NormalizedManagedModelPathResult(normalizedModelPath, selectedContent, cachedModels, false);
            }

            ModelLibraryManifestFile? manifest;
            try
            {
                manifest = JsonUtility.FromJson<ModelLibraryManifestFile>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return new NormalizedManagedModelPathResult(normalizedModelPath, selectedContent, cachedModels, false);
            }

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.id))
            {
                return new NormalizedManagedModelPathResult(normalizedModelPath, selectedContent, cachedModels, false);
            }

            var resolvedTitle = ResolvePreferredTitle(
                normalizedModelPath,
                preferredDisplayName,
                manifest.title);
            if (string.IsNullOrWhiteSpace(resolvedTitle))
            {
                resolvedTitle = "model";
            }

            var preferredDirectory = modelLibraryPaths.GetPreferredItemDirectory(manifest.id, resolvedTitle);
            var changed = false;
            if (!PathsEqual(currentDirectory, preferredDirectory) && !Directory.Exists(preferredDirectory))
            {
                Directory.Move(currentDirectory, preferredDirectory);
                currentDirectory = preferredDirectory;
                normalizedModelPath = modelLibraryPaths.GetModelPathForDirectory(currentDirectory);
                changed = true;
            }
            else
            {
                normalizedModelPath = modelLibraryPaths.GetModelPathForDirectory(currentDirectory);
            }

            if (!string.Equals(manifest.title, resolvedTitle, StringComparison.Ordinal))
            {
                manifest.title = resolvedTitle;
                File.WriteAllText(
                    modelLibraryPaths.GetManifestPathForDirectory(currentDirectory),
                    JsonUtility.ToJson(manifest, true));
                changed = true;
            }

            if (selectedContent != null
                && string.Equals(selectedContent.Data, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedContent = selectedContent with { Data = normalizedModelPath };
                changed = true;
            }

            var updatedCachedModels = cachedModels
                .Select(model =>
                {
                    var nextPath = string.Equals(model.Path, modelPath, StringComparison.OrdinalIgnoreCase)
                        ? normalizedModelPath
                        : model.Path;
                    var nextDisplayName = string.Equals(model.Path, modelPath, StringComparison.OrdinalIgnoreCase)
                                           && !string.IsNullOrWhiteSpace(preferredDisplayName)
                        ? preferredDisplayName.Trim()
                        : model.DisplayName;
                    return model with
                    {
                        Path = nextPath,
                        DisplayName = nextDisplayName,
                    };
                })
                .ToArray();
            if (!changed && !string.Equals(normalizedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
            }

            return new NormalizedManagedModelPathResult(normalizedModelPath, selectedContent, updatedCachedModels, changed);
        }

        private static string ResolvePreferredTitle(string modelPath, string preferredDisplayName, string manifestTitle)
        {
            if (!LooksMachineGeneratedTitle(preferredDisplayName))
            {
                return preferredDisplayName.Trim();
            }

            if (VrmMetadataNameProbe.TryReadDisplayName(modelPath, out var metadataName)
                && !string.IsNullOrWhiteSpace(metadataName))
            {
                return metadataName;
            }

            if (!LooksMachineGeneratedTitle(manifestTitle))
            {
                return manifestTitle.Trim();
            }

            return preferredDisplayName.Trim();
        }

        private static bool LooksMachineGeneratedTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var trimmed = value.Trim();
            var hasLetter = false;
            foreach (var character in trimmed)
            {
                if (char.IsLetter(character))
                {
                    hasLetter = true;
                    break;
                }
            }

            return !hasLetter;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class ModelLibraryManifestFile
        {
            public int schemaVersion = 2;
            public string id = string.Empty;
            public string title = string.Empty;
            public string type = "Model";
            public string entry = "model.vrm";
            public string preview = string.Empty;
            public string thumbnail = string.Empty;
            public string source = string.Empty;
            public string sourceId = string.Empty;
            public string author = string.Empty;
            public string description = string.Empty;
            public string ageRating = "Unknown";
            public string importedAt = string.Empty;
            public string fingerprint = string.Empty;
            public string[] tags = Array.Empty<string>();
        }

        private readonly struct NormalizedManagedModelPathResult
        {
            public NormalizedManagedModelPathResult(
                string normalizedModelPath,
                SelectedContentState? selectedContent,
                System.Collections.Generic.IReadOnlyList<CachedModelState> cachedModels,
                bool changed)
            {
                NormalizedModelPath = normalizedModelPath;
                SelectedContent = selectedContent;
                CachedModels = cachedModels;
                Changed = changed;
            }

            public string NormalizedModelPath { get; }

            public SelectedContentState? SelectedContent { get; }

            public System.Collections.Generic.IReadOnlyList<CachedModelState> CachedModels { get; }

            public bool Changed { get; }
        }
    }
}
