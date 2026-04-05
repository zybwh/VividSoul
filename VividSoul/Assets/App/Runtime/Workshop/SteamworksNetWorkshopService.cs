#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.Workshop
{
    public sealed class SteamworksNetWorkshopService : IWorkshopService
    {
        private const int InstallPathBufferSize = 2048;

        private readonly FileSystemContentCatalog contentCatalog;
        private readonly ISteamPlatformService steamPlatformService;

        public SteamworksNetWorkshopService(
            ISteamPlatformService steamPlatformService,
            FileSystemContentCatalog contentCatalog)
        {
            this.steamPlatformService = steamPlatformService ?? throw new ArgumentNullException(nameof(steamPlatformService));
            this.contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
        }

        public Task<IReadOnlyList<WorkshopContentItem>> GetSubscribedContentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!steamPlatformService.IsInitialized)
            {
                throw new InvalidOperationException("Steam is not initialized.");
            }

            var count = SteamUGC.GetNumSubscribedItems();
            if (count == 0)
            {
                return Task.FromResult<IReadOnlyList<WorkshopContentItem>>(Array.Empty<WorkshopContentItem>());
            }

            var publishedFileIds = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(publishedFileIds, count);

            var workshopContent = new List<WorkshopContentItem>();
            foreach (var publishedFileId in publishedFileIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = SteamUGC.GetItemState(publishedFileId);
                var installDirectory = GetInstalledDirectory(publishedFileId);
                if (string.IsNullOrWhiteSpace(installDirectory))
                {
                    TryTriggerDownloadIfNeeded(publishedFileId, state);
                    continue;
                }

                var contentItems = contentCatalog.Scan(installDirectory, ContentSource.Workshop);
                foreach (var contentItem in contentItems)
                {
                    workshopContent.Add(new WorkshopContentItem(
                        publishedFileId.m_PublishedFileId,
                        state,
                        NeedsUpdate(state),
                        installDirectory,
                        contentItem with
                        {
                            Id = $"workshop:{publishedFileId.m_PublishedFileId}:{contentItem.Id}",
                        }));
                }
            }

            return Task.FromResult<IReadOnlyList<WorkshopContentItem>>(workshopContent
                .OrderBy(item => item.ContentItem.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        private static string? GetInstalledDirectory(PublishedFileId_t publishedFileId)
        {
            var installed = SteamUGC.GetItemInstallInfo(
                publishedFileId,
                out _,
                out string installDirectory,
                InstallPathBufferSize,
                out _);

            return installed && !string.IsNullOrWhiteSpace(installDirectory) && Directory.Exists(installDirectory)
                ? installDirectory
                : null;
        }

        private static void TryTriggerDownloadIfNeeded(PublishedFileId_t publishedFileId, uint itemState)
        {
            if (IsInstalled(itemState) && !NeedsUpdate(itemState))
            {
                return;
            }

            SteamUGC.DownloadItem(publishedFileId, true);
        }

        private static bool IsInstalled(uint itemState)
        {
            return (itemState & (uint)EItemState.k_EItemStateInstalled) != 0;
        }

        private static bool NeedsUpdate(uint itemState)
        {
            return (itemState & (uint)EItemState.k_EItemStateNeedsUpdate) != 0;
        }
    }
}
