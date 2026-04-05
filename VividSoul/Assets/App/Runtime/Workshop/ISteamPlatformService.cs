#nullable enable

namespace VividSoul.Runtime.Workshop
{
    public interface ISteamPlatformService
    {
        bool IsInitialized { get; }

        uint AppId { get; }
    }
}
