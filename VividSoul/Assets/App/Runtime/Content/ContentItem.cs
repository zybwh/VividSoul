#nullable enable

using System.Collections.Generic;

namespace VividSoul.Runtime.Content
{
    public sealed record ContentItem(
        string Id,
        ContentSource Source,
        ContentType Type,
        string RootPath,
        string EntryPath,
        string Title,
        string Description,
        string PreviewPath,
        string ThumbnailPath,
        string AgeRating,
        IReadOnlyList<string> Tags);
}
