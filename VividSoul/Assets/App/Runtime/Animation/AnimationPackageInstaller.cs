#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Animation
{
    public sealed class AnimationPackageInstaller
    {
        public async Task<AnimationPackage> InstallAsync(
            ContentItem animationContent,
            DesktopPetAnimationController animationController,
            CancellationToken cancellationToken = default)
        {
            if (animationContent == null)
            {
                throw new ArgumentNullException(nameof(animationContent));
            }

            if (animationController == null)
            {
                throw new ArgumentNullException(nameof(animationController));
            }

            if (animationContent.Type != ContentType.Animation)
            {
                throw new InvalidOperationException("Only animation content packages can be installed as animation packages.");
            }

            var package = CreatePackage(animationContent.RootPath, animationContent.EntryPath);
            await animationController.ApplyAnimationPackageAsync(package, cancellationToken);
            return package;
        }

        public AnimationPackage CreatePackage(string rootPath, string preferredEntryPath = "")
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("An animation package root path is required.", nameof(rootPath));
            }

            if (!Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException($"Animation package root was not found: {rootPath}");
            }

            var animationPaths = Directory
                .EnumerateFiles(rootPath, "*.vrma", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (animationPaths.Length == 0)
            {
                throw new InvalidOperationException("Animation package does not contain any .vrma files.");
            }

            var entryPath = ResolveEntryPath(animationPaths, preferredEntryPath);
            var idlePath = FindNamedAnimation(animationPaths, "idle") ?? entryPath;
            var clickPath = FindNamedAnimation(animationPaths, "click");
            var posePath = FindNamedAnimation(animationPaths, "pose");

            return new AnimationPackage(
                RootPath: rootPath,
                EntryPath: entryPath,
                IdleAnimationPath: idlePath,
                ClickAnimationPath: clickPath ?? string.Empty,
                PoseAnimationPath: posePath ?? string.Empty,
                AnimationPaths: animationPaths);
        }

        private static string ResolveEntryPath(IReadOnlyList<string> animationPaths, string preferredEntryPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredEntryPath))
            {
                var resolved = animationPaths.FirstOrDefault(path =>
                    string.Equals(path, preferredEntryPath, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return animationPaths[0];
        }

        private static string? FindNamedAnimation(IEnumerable<string> animationPaths, string keyword)
        {
            return animationPaths.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}
