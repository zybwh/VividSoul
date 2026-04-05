#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;

namespace VividSoul.Runtime.Animation
{
    public sealed class VrmaAnimationLoaderService : IAnimationLoader
    {
        public async Task<Vrm10AnimationInstance> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An animation path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The animation file does not exist.", path);
            }

            if (!string.Equals(Path.GetExtension(path), ".vrma", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported animation extension: {Path.GetExtension(path)}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var gltfData = new GlbLowLevelParser(path, bytes).Parse();
            using var loader = new VrmAnimationImporter(gltfData);
            var gltfInstance = await loader.LoadAsync(new ImmediateCaller());

            cancellationToken.ThrowIfCancellationRequested();

            if (!gltfInstance.TryGetComponent<Vrm10AnimationInstance>(out var animationInstance))
            {
                throw new InvalidOperationException("Failed to create a VRMA runtime instance.");
            }

            animationInstance.ShowBoxMan(false);
            animationInstance.gameObject.name = $"VRMA:{Path.GetFileNameWithoutExtension(path)}";
            return animationInstance;
        }
    }
}
