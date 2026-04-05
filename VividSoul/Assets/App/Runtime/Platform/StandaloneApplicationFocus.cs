#nullable enable

using System;

namespace VividSoul.Runtime.Platform
{
    internal static class StandaloneApplicationFocus
    {
        public static void Request()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            try
            {
                MacOsApplicationFocus.ActivateIgnoringOtherApps();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[StandaloneApplicationFocus] macOS activate failed: {exception.Message}");
            }
#endif
        }
    }
}
