#nullable enable

using System;
using System.IO;
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
    }
}
