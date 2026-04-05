#nullable enable

using System;
using System.IO;
using System.Linq;

namespace VividSoul.Runtime.Settings
{
    public sealed class CachedModelStore
    {
        private const int MaxCachedModelCount = 16;

        private readonly IDesktopPetSettingsStore settingsStore;

        public CachedModelStore(IDesktopPetSettingsStore settingsStore)
        {
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public CachedModelState[] Load()
        {
            return settingsStore.Load().CachedModels.ToArray();
        }

        public void Remember(string displayName, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A cached model path is required.", nameof(path));
            }

            var normalizedPath = Path.GetFullPath(path);
            var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(normalizedPath)
                : displayName.Trim();
            var settings = settingsStore.Load();
            var cachedModels = settings.CachedModels
                .Where(static model => !string.IsNullOrWhiteSpace(model.Path))
                .Where(model => File.Exists(model.Path))
                .Where(model => !string.Equals(model.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(new CachedModelState(resolvedDisplayName, normalizedPath))
                .Take(MaxCachedModelCount)
                .ToArray();

            settingsStore.Save(settings with { CachedModels = cachedModels });
        }
    }
}
