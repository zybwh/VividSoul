#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.AI
{
    public sealed class MateConversationService : IDisposable
    {
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly IMateConversationBackend localBackend;
        private readonly IMateConversationBackend openClawBackend;
        private IMateConversationBackend? activeBackend;
        private string activeProviderId = string.Empty;
        private string activeProviderSignature = string.Empty;
        private string activeCharacterSourcePath = string.Empty;
        private string activeCharacterDisplayName = string.Empty;

        public MateConversationService(
            IAiSettingsStore aiSettingsStore,
            IMateConversationBackend localBackend,
            IMateConversationBackend openClawBackend)
        {
            this.aiSettingsStore = aiSettingsStore ?? throw new ArgumentNullException(nameof(aiSettingsStore));
            this.localBackend = localBackend ?? throw new ArgumentNullException(nameof(localBackend));
            this.openClawBackend = openClawBackend ?? throw new ArgumentNullException(nameof(openClawBackend));

            AttachBackend(this.localBackend);
            AttachBackend(this.openClawBackend);
        }

        public event Action<ConversationMessageEnvelope>? MessageReceived;

        public event Action<ConversationStatusSnapshot>? StatusChanged;

        public ConversationStatusSnapshot? CurrentStatus { get; private set; }

        public async Task SynchronizeAsync(
            string characterSourcePath,
            string characterDisplayName,
            CancellationToken cancellationToken = default)
        {
            var activeProfile = ResolveActiveProfile();
            var targetBackend = ResolveBackend(activeProfile.ProviderType);
            var providerSignature = BuildProfileSignature(activeProfile);
            var hasBackendChanged = !ReferenceEquals(activeBackend, targetBackend);
            var hasProviderChanged = !string.Equals(activeProviderSignature, providerSignature, StringComparison.Ordinal);
            var hasCharacterChanged = !string.Equals(activeCharacterSourcePath, characterSourcePath, StringComparison.Ordinal)
                || !string.Equals(activeCharacterDisplayName, characterDisplayName, StringComparison.Ordinal);

            if (!hasBackendChanged && !hasProviderChanged && !hasCharacterChanged)
            {
                return;
            }

            if (hasBackendChanged && activeBackend != null)
            {
                await activeBackend.DeactivateAsync(cancellationToken);
            }

            activeBackend = targetBackend;
            activeProviderId = activeProfile.Id;
            activeProviderSignature = providerSignature;
            activeCharacterSourcePath = characterSourcePath ?? string.Empty;
            activeCharacterDisplayName = characterDisplayName ?? string.Empty;
            await activeBackend.ActivateAsync(
                activeProfile,
                activeCharacterSourcePath,
                activeCharacterDisplayName,
                cancellationToken);
        }

        public async Task SendUserMessageAsync(
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(characterSourcePath))
            {
                throw new UserFacingException("当前还没有加载角色，暂时无法发起对话。");
            }

            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A chat message is required.", nameof(userMessage));
            }

            await SynchronizeAsync(characterSourcePath, characterDisplayName, cancellationToken);
            var activeProfile = ResolveActiveProfile();
            var backend = activeBackend ?? ResolveBackend(activeProfile.ProviderType);
            await backend.SendUserMessageAsync(
                activeProfile,
                characterSourcePath,
                characterDisplayName,
                userMessage,
                cancellationToken);
        }

        public async Task ClearCharacterContextAsync(CancellationToken cancellationToken = default)
        {
            activeProviderId = string.Empty;
            activeCharacterSourcePath = string.Empty;
            activeCharacterDisplayName = string.Empty;
            activeProviderSignature = string.Empty;
            if (activeBackend != null)
            {
                await activeBackend.DeactivateAsync(cancellationToken);
            }
        }

        public void MarkMessagesRead()
        {
            activeBackend?.MarkMessagesRead();
        }

        public void Dispose()
        {
            localBackend.Dispose();
            openClawBackend.Dispose();
        }

        private void AttachBackend(IMateConversationBackend backend)
        {
            backend.MessageReceived += HandleBackendMessageReceived;
            backend.StatusChanged += HandleBackendStatusChanged;
        }

        private void HandleBackendMessageReceived(ConversationMessageEnvelope envelope)
        {
            if (activeBackend == null)
            {
                return;
            }

            MessageReceived?.Invoke(envelope);
        }

        private void HandleBackendStatusChanged(ConversationStatusSnapshot status)
        {
            if (!string.IsNullOrWhiteSpace(activeProviderId)
                && !string.Equals(status.ProviderId, activeProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CurrentStatus = status;
            StatusChanged?.Invoke(status);
        }

        private IMateConversationBackend ResolveBackend(LlmProviderType providerType)
        {
            return providerType switch
            {
                LlmProviderType.OpenClaw => openClawBackend,
                _ => localBackend,
            };
        }

        private LlmProviderProfile ResolveActiveProfile()
        {
            var settings = aiSettingsStore.Load();
            var activeProfile = settings.ProviderProfiles.FirstOrDefault(profile =>
                                   string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                               ?? settings.ProviderProfiles.FirstOrDefault();
            if (activeProfile == null)
            {
                throw new UserFacingException("当前还没有可用的 LLM Provider 配置。");
            }

            if (!activeProfile.Enabled)
            {
                throw new UserFacingException("当前激活的 Provider 处于禁用状态。");
            }

            return activeProfile;
        }

        private static string BuildProfileSignature(LlmProviderProfile profile)
        {
            return string.Join(
                "|",
                profile.Id,
                profile.ProviderType.ToString(),
                profile.BaseUrl,
                profile.Model,
                profile.Enabled.ToString(),
                profile.OpenClawGatewayWsUrl,
                profile.OpenClawAgentId,
                profile.OpenClawSessionMode.ToString(),
                profile.OpenClawSessionKeyTemplate,
                profile.OpenClawAutoConnect.ToString(),
                profile.OpenClawAutoReconnect.ToString(),
                profile.OpenClawReceiveProactiveMessages.ToString(),
                profile.OpenClawMirrorTranscriptLocally.ToString(),
                profile.OpenClawEnableBubbleForIncoming.ToString(),
                profile.OpenClawEnableTtsForIncoming.ToString());
        }
    }
}
