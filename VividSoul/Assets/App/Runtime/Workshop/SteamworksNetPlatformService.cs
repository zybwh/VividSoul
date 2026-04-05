#nullable enable

using System;

namespace VividSoul.Runtime.Workshop
{
    public sealed class SteamworksNetPlatformService : ISteamPlatformService
    {
        private readonly uint appId;

        public SteamworksNetPlatformService(uint appId)
        {
            this.appId = appId;
        }

        public bool IsInitialized => SteamManager.Initialized;

        public uint AppId => appId;

        public void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Steam is not initialized.");
            }
        }
    }
}
