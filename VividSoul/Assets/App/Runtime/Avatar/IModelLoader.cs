#nullable enable

using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VividSoul.Runtime.Avatar
{
    public interface IModelLoader
    {
        Task<ModelLoadResult> LoadAsync(string path, Transform parent, CancellationToken cancellationToken = default);
    }

    public sealed record ModelLoadResult(
        string SourcePath,
        GameObject Root,
        string DisplayName,
        string Author,
        string Version,
        Texture2D? Thumbnail);
}
