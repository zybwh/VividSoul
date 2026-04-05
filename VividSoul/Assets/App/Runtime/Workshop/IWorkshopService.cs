#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.Workshop
{
    public interface IWorkshopService
    {
        Task<IReadOnlyList<WorkshopContentItem>> GetSubscribedContentAsync(CancellationToken cancellationToken = default);
    }
}
