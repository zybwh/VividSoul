#nullable enable

namespace VividSoul.Runtime.Content
{
    public sealed record ModelImportResult(
        ContentItem Item,
        string Fingerprint,
        bool ImportedNewItem);
}
