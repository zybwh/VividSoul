#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.AI
{
    public sealed class MateConversationOrchestrator
    {
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly IAiSecretsStore aiSecretsStore;
        private readonly IChatSessionStore chatSessionStore;
        private readonly ILlmUsageStatsStore llmUsageStatsStore;
        private readonly ModelFingerprintService modelFingerprintService;
        private readonly ILlmProvider openAiCompatibleProvider;
        private readonly ILlmProvider miniMaxProvider;

        public MateConversationOrchestrator(
            IAiSettingsStore aiSettingsStore,
            IAiSecretsStore aiSecretsStore,
            IChatSessionStore chatSessionStore,
            ILlmUsageStatsStore llmUsageStatsStore,
            ModelFingerprintService modelFingerprintService)
        {
            this.aiSettingsStore = aiSettingsStore ?? throw new ArgumentNullException(nameof(aiSettingsStore));
            this.aiSecretsStore = aiSecretsStore ?? throw new ArgumentNullException(nameof(aiSecretsStore));
            this.chatSessionStore = chatSessionStore ?? throw new ArgumentNullException(nameof(chatSessionStore));
            this.llmUsageStatsStore = llmUsageStatsStore ?? throw new ArgumentNullException(nameof(llmUsageStatsStore));
            this.modelFingerprintService = modelFingerprintService ?? throw new ArgumentNullException(nameof(modelFingerprintService));
            openAiCompatibleProvider = new OpenAiCompatibleLlmProvider();
            miniMaxProvider = new MiniMaxLlmProvider();
        }

        public async Task<LlmResponseEnvelope> GenerateReplyAsync(
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(characterSourcePath))
            {
                throw new UserFacingException("当前没有可用角色，无法发起对话。");
            }

            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A user message is required.", nameof(userMessage));
            }

            var settings = aiSettingsStore.Load();
            var activeProvider = ResolveActiveProvider(settings);
            var apiKey = aiSecretsStore.LoadApiKey(activeProvider.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UserFacingException("当前 Provider 的 API Key 为空。请在设置里填写后点击“保存设置”，再回来发送消息。");
            }

            var modelFingerprint = modelFingerprintService.ComputeSha256(characterSourcePath);
            var sessionId = BuildSessionId(modelFingerprint);
            var session = chatSessionStore.Load(sessionId, modelFingerprint);
            var pendingMessages = BuildPendingMessages(session, settings, userMessage.Trim(), sessionId);
            var systemPrompt = BuildSystemPrompt(settings.GlobalSystemPrompt, characterDisplayName);
            var requestContext = new LlmRequestContext(
                ProviderProfile: activeProvider,
                ApiKey: apiKey,
                SystemPrompt: systemPrompt,
                Temperature: settings.Temperature,
                MaxOutputTokens: settings.MaxOutputTokens,
                EnableStreaming: false,
                Messages: pendingMessages);
            var provider = ResolveProvider(activeProvider.ProviderType);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await provider.GenerateAsync(requestContext, cancellationToken);
                stopwatch.Stop();

                var assistantMessage = new ChatMessage(
                    Id: Guid.NewGuid().ToString("N"),
                    SessionId: sessionId,
                    Role: ChatRole.Assistant,
                    Text: response.DisplayText,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Source: ChatInvocationSource.UserInput);
                var savedMessages = TrimMessages(settings, pendingMessages.Concat(new[] { assistantMessage }).ToArray());
                chatSessionStore.Save(new ChatSessionData(sessionId, modelFingerprint, savedMessages));
                llmUsageStatsStore.RecordSuccess(
                    response.ProviderId,
                    response.Model,
                    stopwatch.ElapsedMilliseconds,
                    response.PromptCharacters,
                    response.CompletionCharacters);
                return response;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stopwatch.Stop();
                chatSessionStore.Save(new ChatSessionData(sessionId, modelFingerprint, pendingMessages));
                llmUsageStatsStore.RecordFailure(
                    activeProvider.Id,
                    activeProvider.Model,
                    exception.Message,
                    stopwatch.ElapsedMilliseconds,
                    pendingMessages.Sum(static message => message.Text.Length));
                throw;
            }
        }

        private static LlmProviderProfile ResolveActiveProvider(AiSettingsData settings)
        {
            var provider = settings.ProviderProfiles.FirstOrDefault(profile =>
                               string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                           ?? settings.ProviderProfiles.FirstOrDefault();
            if (provider == null)
            {
                throw new UserFacingException("当前还没有可用的 LLM Provider 配置。");
            }

            if (!provider.Enabled)
            {
                throw new UserFacingException("当前激活的 Provider 处于禁用状态。");
            }

            if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            {
                throw new UserFacingException("当前 Provider 还没有填写 API URL。");
            }

            if (string.IsNullOrWhiteSpace(provider.Model))
            {
                throw new UserFacingException("当前 Provider 还没有填写模型名称。");
            }

            return provider;
        }

        private ILlmProvider ResolveProvider(LlmProviderType providerType)
        {
            return providerType switch
            {
                LlmProviderType.OpenAiCompatible => openAiCompatibleProvider,
                LlmProviderType.MiniMax => miniMaxProvider,
                _ => throw new UserFacingException($"当前版本只接通了 OpenAI-compatible 和 MiniMax Provider，暂不支持 {providerType}。"),
            };
        }

        private static ChatMessage[] BuildPendingMessages(
            ChatSessionData session,
            AiSettingsData settings,
            string userMessage,
            string sessionId)
        {
            var recentMessages = TrimMessages(settings, session.Messages);
            var newMessage = new ChatMessage(
                Id: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatRole.User,
                Text: userMessage,
                CreatedAt: DateTimeOffset.UtcNow,
                Source: ChatInvocationSource.UserInput);
            return recentMessages.Concat(new[] { newMessage }).ToArray();
        }

        private static ChatMessage[] TrimMessages(AiSettingsData settings, System.Collections.Generic.IEnumerable<ChatMessage> messages)
        {
            var maxMessages = Math.Max(4, settings.MemoryWindowTurns * 2);
            return messages
                .Where(static message => message != null)
                .TakeLast(maxMessages)
                .ToArray();
        }

        private static string BuildSystemPrompt(string globalSystemPrompt, string characterDisplayName)
        {
            var prompt = globalSystemPrompt?.Trim() ?? string.Empty;
            const string ResponseFormatInstruction = "Reply as natural spoken dialogue instead of markdown or document style. Do not use headings, bullet lists, numbered lists, code fences, markdown emphasis, XML-style tags, or roleplay markers. Keep the reply in one short paragraph with no unnecessary line breaks, and prefer concise colloquial Chinese when the user is speaking Chinese.";
            if (string.IsNullOrWhiteSpace(characterDisplayName))
            {
                return string.IsNullOrWhiteSpace(prompt)
                    ? ResponseFormatInstruction
                    : $"{prompt}\n{ResponseFormatInstruction}";
            }

            var personaLine = $"Current desktop mate character: {characterDisplayName.Trim()}.";
            return string.IsNullOrWhiteSpace(prompt)
                ? $"{personaLine}\n{ResponseFormatInstruction}"
                : $"{prompt}\n{personaLine}\n{ResponseFormatInstruction}";
        }

        private static string BuildSessionId(string characterFingerprint)
        {
            var normalized = string.IsNullOrWhiteSpace(characterFingerprint)
                ? "default-character"
                : characterFingerprint.Trim();
            return $"chat-{normalized}";
        }
    }
}
