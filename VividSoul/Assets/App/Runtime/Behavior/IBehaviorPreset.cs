#nullable enable

namespace VividSoul.Runtime.Behavior
{
    public interface IBehaviorPreset
    {
        string Name { get; }

        string RootPath { get; }

        BehaviorMovementPreset Movement { get; }

        string IdleAnimationPath { get; }

        string ClickAnimationPath { get; }

        string PoseAnimationPath { get; }

        bool TryGetActionAnimationPath(string key, out string animationPath);

        bool TryGetPoseAnimationPath(string key, out string animationPath);

        bool TryGetExpression(string key, out string expression);
    }
}
