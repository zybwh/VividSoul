#nullable enable

namespace VividSoul.Runtime.Behavior
{
    public sealed record BehaviorMovementPreset(
        BehaviorMovementType Type,
        string StartAnimationPath,
        string LoopAnimationPath,
        string StopAnimationPath,
        string LoopVerticalAnimationPath,
        float SpeedMultiplier,
        bool FaceVelocity)
    {
        public static BehaviorMovementPreset Default { get; } = new(
            BehaviorMovementType.Walk,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            1f,
            true);

        public float ResolvedSpeedMultiplier => SpeedMultiplier > 0f ? SpeedMultiplier : 1f;
    }
}
