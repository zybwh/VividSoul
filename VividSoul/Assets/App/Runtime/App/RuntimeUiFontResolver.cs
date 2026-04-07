#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividSoul.Runtime.App
{
    public static class RuntimeUiFontResolver
    {
        private const int DefaultPointSize = 16;

        private static readonly string[] SystemFontCandidates =
        {
            "PingFang SC",
            "Hiragino Sans GB",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Arial Unicode MS",
            "Arial",
        };

        private static readonly Dictionary<int, Font> CachedFonts = new();
        private static readonly object SyncRoot = new();

        private static HashSet<string>? installedFontNames;

        public static Font GetFont(int preferredPointSize = DefaultPointSize)
        {
            var resolvedPointSize = preferredPointSize > 0 ? preferredPointSize : DefaultPointSize;

            lock (SyncRoot)
            {
                if (CachedFonts.TryGetValue(resolvedPointSize, out var cachedFont))
                {
                    return cachedFont;
                }

                var resolvedFont = TryCreateSystemFont(resolvedPointSize)
                                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                CachedFonts[resolvedPointSize] = resolvedFont;
                return resolvedFont;
            }
        }

        private static Font? TryCreateSystemFont(int preferredPointSize)
        {
            var availableFonts = GetInstalledFontNames();
            foreach (var candidate in SystemFontCandidates)
            {
                if (availableFonts.Contains(candidate))
                {
                    return Font.CreateDynamicFontFromOSFont(candidate, preferredPointSize);
                }
            }

            return null;
        }

        private static HashSet<string> GetInstalledFontNames()
        {
            installedFontNames ??= new HashSet<string>(
                Font.GetOSInstalledFontNames(),
                StringComparer.OrdinalIgnoreCase);
            return installedFontNames;
        }
    }
}
