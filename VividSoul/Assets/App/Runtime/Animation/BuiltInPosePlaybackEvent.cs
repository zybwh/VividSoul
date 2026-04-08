#nullable enable

namespace VividSoul.Runtime.Animation
{
    public sealed record BuiltInPosePlaybackEvent(
        string PoseId,
        bool UseCatalogBubble);
}
