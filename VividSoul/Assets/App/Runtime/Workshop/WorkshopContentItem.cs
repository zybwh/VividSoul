#nullable enable

using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Workshop
{
    public sealed record WorkshopContentItem(
        ulong PublishedFileId,
        uint ItemState,
        bool NeedsUpdate,
        string InstallDirectory,
        ContentItem ContentItem);
}
