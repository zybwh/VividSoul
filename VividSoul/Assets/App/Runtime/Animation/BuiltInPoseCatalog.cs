#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace VividSoul.Runtime.Animation
{
    public static class BuiltInPoseCatalog
    {
        private static readonly BuiltInPoseOption[] BuiltInPoses =
        {
            new("vrma_01", "VRMA_01 Show full body", "Defaults/Animations/VRMA_MotionPack/VRMA_01.vrma", "展示全身的短动作，适合轻微展示当前角色造型。"),
            new("vrma_02", "VRMA_02 Greeting", "Defaults/Animations/VRMA_MotionPack/VRMA_02.vrma", "打招呼或轻回应时使用的问候动作。"),
            new("vrma_03", "VRMA_03 Peace sign", "Defaults/Animations/VRMA_MotionPack/VRMA_03.vrma", "轻松、可爱、带一点庆祝感时使用的比耶动作。"),
            new("vrma_04", "VRMA_04 Shoot", "Defaults/Animations/VRMA_MotionPack/VRMA_04.vrma", "偏夸张、俏皮的指向或开场动作，正常聊天中应谨慎使用。"),
            new("vrma_05", "VRMA_05 Spin", "Defaults/Animations/VRMA_MotionPack/VRMA_05.vrma", "明显活跃或炫耀感较强的转圈动作，只在少数兴奋场景使用。"),
            new("vrma_06", "VRMA_06 Model pose", "Defaults/Animations/VRMA_MotionPack/VRMA_06.vrma", "安静展示、摆拍或优雅停顿时使用的模特姿势。"),
            new("vrma_07", "VRMA_07 Squat", "Defaults/Animations/VRMA_MotionPack/VRMA_07.vrma", "强风格化动作，通常不用于普通聊天回复。"),
        };

        public static IReadOnlyList<BuiltInPoseOption> All => BuiltInPoses;

        public static BuiltInPoseOption FindRequired(string poseId)
        {
            return BuiltInPoses.FirstOrDefault(pose =>
                       string.Equals(pose.Id, poseId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Built-in pose '{poseId}' was not found.");
        }
    }
}
