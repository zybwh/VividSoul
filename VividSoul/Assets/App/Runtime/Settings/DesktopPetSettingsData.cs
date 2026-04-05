#nullable enable

namespace VividSoul.Runtime.Settings
{
    public sealed record DesktopPetSettingsData(
        SelectedContentState? SelectedContent,
        System.Collections.Generic.IReadOnlyList<CachedModelState> CachedModels,
        float PositionX,
        float PositionY,
        float Scale,
        float RotationY,
        bool IsTopMost,
        bool IsClickThrough,
        int MonitorIndex,
        float VoiceVolume,
        bool HeadFollowEnabled,
        bool HandFollowEnabled);
}
