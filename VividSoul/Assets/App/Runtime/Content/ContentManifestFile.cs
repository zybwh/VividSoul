#nullable enable

using System;

namespace VividSoul.Runtime.Content
{
    [Serializable]
    internal sealed class ContentManifestFile
    {
        public int schemaVersion = 1;
        public string type = string.Empty;
        public string title = string.Empty;
        public string description = string.Empty;
        public string entry = string.Empty;
        public string preview = string.Empty;
        public string thumbnail = string.Empty;
        public string ageRating = "Everyone";
        public string[] tags = Array.Empty<string>();
    }
}
