#nullable enable

using System.Threading;
using System.Threading.Tasks;
using UniVRM10;

namespace VividSoul.Runtime.Animation
{
    public interface IAnimationLoader
    {
        Task<Vrm10AnimationInstance> LoadAsync(string path, CancellationToken cancellationToken = default);
    }
}
