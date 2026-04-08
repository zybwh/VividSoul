#nullable enable

using VividSoul.Runtime.Avatar;

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
        bool HandFollowEnabled,
        bool CompactWindowEnabled,
        bool HasWindowPosition,
        float WindowPositionX,
        float WindowPositionY,
        VrmImportPerformanceMode VrmImportPerformanceMode);
}
