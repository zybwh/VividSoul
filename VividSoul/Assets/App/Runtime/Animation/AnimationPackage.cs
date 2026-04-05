#nullable enable

using System.Collections.Generic;

namespace VividSoul.Runtime.Animation
{
    public sealed record AnimationPackage(
        string RootPath,
        string EntryPath,
        string IdleAnimationPath,
        string ClickAnimationPath,
        string PoseAnimationPath,
        IReadOnlyList<string> AnimationPaths);
}
