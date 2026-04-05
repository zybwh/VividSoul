#nullable enable

using System.Collections.Generic;

namespace VividSoul.Runtime.Content
{
    public sealed record ContentManifest(
        int SchemaVersion,
        ContentType Type,
        string Title,
        string Description,
        string EntryRelativePath,
        string PreviewRelativePath,
        string ThumbnailRelativePath,
        string AgeRating,
        IReadOnlyList<string> Tags);
}
