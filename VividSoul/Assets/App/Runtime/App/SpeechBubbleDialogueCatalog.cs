#nullable enable

using System;
using System.Collections.Generic;

namespace VividSoul.Runtime.App
{
    public static class SpeechBubbleDialogueCatalog
    {
        private static readonly IReadOnlyDictionary<string, string> BuiltInPoseLines =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["vrma_01"] = "先让我正式亮个相吧。",
                ["vrma_02"] = "嗨，今天也请多关照。",
                ["vrma_03"] = "耶，这个手势是不是很有精神？",
                ["vrma_04"] = "目标锁定，准备发射可爱光波。",
                ["vrma_05"] = "转一圈，看看我今天的状态。",
                ["vrma_06"] = "这个角度不错，拍照一定很上镜。",
                ["vrma_07"] = "先蹲一下，我要开始认真了。",
            };

        public static bool TryGetBuiltInPoseLine(string poseId, out string line)
        {
            if (string.IsNullOrWhiteSpace(poseId))
            {
                line = string.Empty;
                return false;
            }

            return BuiltInPoseLines.TryGetValue(poseId, out line!);
        }
    }
}
