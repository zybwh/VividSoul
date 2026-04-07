#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.AI
{
    public sealed class LocalLlmConversationBackend : IMateConversationBackend
    {
        private readonly MateConversationOrchestrator mateConversationOrchestrator;
        private ConversationStatusSnapshot lastStatus = new(
            ProviderId: string.Empty,
            ProviderDisplayName: string.Empty,
            ProviderType: LlmProviderType.OpenAiCompatible,
            ConnectionState: ConversationConnectionState.Disconnected,
            StatusText: "未激活",
            SessionKey: string.Empty,
            AgentId: string.Empty,
            IsRequestInFlight: false,
            UnreadCount: 0);

        public LocalLlmConversationBackend(MateConversationOrchestrator mateConversationOrchestrator)
        {
            this.mateConversationOrchestrator = mateConversationOrchestrator ?? throw new ArgumentNullException(nameof(mateConversationOrchestrator));
        }

        public event Action<ConversationMessageEnvelope>? MessageReceived;

        public event Action<ConversationStatusSnapshot>? StatusChanged;

        public LlmProviderType ProviderType => LlmProviderType.OpenAiCompatible;

        public Task ActivateAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            CancellationToken cancellationToken)
        {
            _ = characterSourcePath;
            _ = characterDisplayName;
            cancellationToken.ThrowIfCancellationRequested();
            PublishStatus(profile, false);
            return Task.CompletedTask;
        }

        public async Task SendUserMessageAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A chat message is required.", nameof(userMessage));
            }

            PublishStatus(profile, true);
            MessageReceived?.Invoke(new ConversationMessageEnvelope(
                Message: new ChatMessage(
                    Id: Guid.NewGuid().ToString("N"),
                    SessionId: BuildLocalSessionId(characterSourcePath),
                    Role: ChatRole.User,
                    Text: userMessage.Trim(),
                    CreatedAt: DateTimeOffset.UtcNow,
                    Source: ChatInvocationSource.UserInput),
                IsProactive: false,
                ShouldDisplayBubble: false,
                ShouldSpeak: false,
                IsOptimistic: true));

            try
            {
                var response = await mateConversationOrchestrator.GenerateReplyAsync(
                    characterSourcePath,
                    characterDisplayName,
                    userMessage,
                    cancellationToken);
                MessageReceived?.Invoke(new ConversationMessageEnvelope(
                    Message: new ChatMessage(
                        Id: Guid.NewGuid().ToString("N"),
                        SessionId: BuildLocalSessionId(characterSourcePath),
                        Role: ChatRole.Assistant,
                        Text: response.DisplayText,
                        CreatedAt: DateTimeOffset.UtcNow,
                        Source: ChatInvocationSource.UserInput),
                    IsProactive: false,
                    ShouldDisplayBubble: true,
                    ShouldSpeak: response.ShouldSpeak,
                    IsOptimistic: false));
            }
            finally
            {
                PublishStatus(profile, false);
            }
        }

        public Task DeactivateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lastStatus.ProviderId))
            {
                return Task.CompletedTask;
            }

            lastStatus = lastStatus with
            {
                ConnectionState = ConversationConnectionState.Disconnected,
                StatusText = "未激活",
                SessionKey = string.Empty,
                AgentId = string.Empty,
                IsRequestInFlight = false,
                UnreadCount = 0,
            };
            StatusChanged?.Invoke(lastStatus);
            return Task.CompletedTask;
        }

        public void MarkMessagesRead()
        {
        }

        public void Dispose()
        {
        }

        private void PublishStatus(LlmProviderProfile profile, bool isRequestInFlight)
        {
            lastStatus = new ConversationStatusSnapshot(
                ProviderId: profile.Id,
                ProviderDisplayName: profile.DisplayName,
                ProviderType: profile.ProviderType,
                ConnectionState: ConversationConnectionState.Connected,
                StatusText: isRequestInFlight ? "本地请求中" : "本地 Provider 已就绪",
                SessionKey: string.Empty,
                AgentId: string.Empty,
                IsRequestInFlight: isRequestInFlight,
                UnreadCount: 0);
            StatusChanged?.Invoke(lastStatus);
        }

        private static string BuildLocalSessionId(string characterSourcePath)
        {
            return string.IsNullOrWhiteSpace(characterSourcePath)
                ? "local-default-session"
                : $"local:{characterSourcePath.GetHashCode():X8}";
        }
    }
}
