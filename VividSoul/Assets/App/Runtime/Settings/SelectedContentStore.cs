#nullable enable

using System;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Settings
{
    public sealed class SelectedContentStore
    {
        private readonly IDesktopPetSettingsStore settingsStore;

        public SelectedContentStore(IDesktopPetSettingsStore settingsStore)
        {
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public SelectedContentState? Load()
        {
            return settingsStore.Load().SelectedContent;
        }

        public void Save(SelectedContentState? selectedContent)
        {
            var settings = settingsStore.Load();
            settingsStore.Save(settings with { SelectedContent = selectedContent });
        }

        public void Save(SelectedContentSource source, ContentType type, string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                throw new ArgumentException("Selected content data is required.", nameof(data));
            }

            Save(new SelectedContentState(source, type, data));
        }

        public void Clear()
        {
            Save((SelectedContentState?)null);
        }
    }
}
