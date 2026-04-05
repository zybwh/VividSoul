#nullable enable

using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Settings
{
    public sealed record SelectedContentState(
        SelectedContentSource Source,
        ContentType Type,
        string Data);
}
