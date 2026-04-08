#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using VividSoul.Runtime.Settings;

namespace VividSoul.Runtime.Avatar
{
    public sealed class VrmModelLoaderService : IModelLoader
    {
        private readonly CharacterRuntimeAssembler characterRuntimeAssembler;
        private readonly IDesktopPetSettingsStore settingsStore;

        public VrmModelLoaderService(CharacterRuntimeAssembler characterRuntimeAssembler, IDesktopPetSettingsStore settingsStore)
        {
            this.characterRuntimeAssembler = characterRuntimeAssembler ?? throw new ArgumentNullException(nameof(characterRuntimeAssembler));
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public async Task<ModelLoadResult> LoadAsync(string path, Transform parent, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A model path is required.", nameof(path));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The model file does not exist.", path);
            }

            if (!string.Equals(Path.GetExtension(path), ".vrm", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported model extension: {Path.GetExtension(path)}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var importSettings = settingsStore.Load();
            var instance = await Vrm10.LoadPathAsync(
                path,
                canLoadVrm0X: true,
                controlRigGenerationOption: ControlRigGenerationOption.Generate,
                showMeshes: true,
                textureDeserializer: CreateTextureDeserializer(importSettings.VrmImportPerformanceMode),
                ct: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (instance == null)
            {
                throw new InvalidOperationException("VRM loading completed without creating an instance.");
            }

            var root = characterRuntimeAssembler.Assemble(instance.gameObject, parent);
            var meta = instance.Vrm != null ? instance.Vrm.Meta : null;
            var displayName = !string.IsNullOrWhiteSpace(meta != null ? meta.Name : null)
                ? meta!.Name
                : Path.GetFileNameWithoutExtension(path);
            var author = meta != null && meta.Authors != null && meta.Authors.Count > 0 && !string.IsNullOrWhiteSpace(meta.Authors[0])
                ? meta.Authors[0]
                : "Unknown";
            var version = !string.IsNullOrWhiteSpace(meta != null ? meta.Version : null)
                ? meta!.Version
                : "Unknown";
            var thumbnail = meta != null ? meta.Thumbnail : null;

            return new ModelLoadResult(path, root, displayName, author, version, thumbnail);
        }

        private static ITextureDeserializer? CreateTextureDeserializer(VrmImportPerformanceMode performanceMode)
        {
            return performanceMode switch
            {
                VrmImportPerformanceMode.Original => null,
                VrmImportPerformanceMode.Balanced => new RuntimeScaledTextureDeserializer(2048),
                VrmImportPerformanceMode.MemorySaver => new RuntimeScaledTextureDeserializer(1024),
                _ => new RuntimeScaledTextureDeserializer(2048),
            };
        }
    }
}
