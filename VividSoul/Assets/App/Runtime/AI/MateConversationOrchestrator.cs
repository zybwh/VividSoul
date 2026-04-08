#nullable enable

using System;
using System.Collections.Generic;
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
        private static readonly TimeSpan ThreadInactivityWindow = TimeSpan.FromHours(24);
        private readonly IAiSettingsStore aiSettingsStore;
        private readonly IAiSecretsStore aiSecretsStore;
        private readonly IChatSessionStore chatSessionStore;
        private readonly ILlmUsageStatsStore llmUsageStatsStore;
        private readonly ModelFingerprintService modelFingerprintService;
        private readonly ILlmProvider openAiCompatibleProvider;
        private readonly ILlmProvider miniMaxProvider;
        private readonly SoulProfileStore soulProfileStore;
        private readonly ReminderStore reminderStore;
        private readonly SoulPromptAssembler soulPromptAssembler;
        private readonly MemoryJudge memoryJudge;

        public MateConversationOrchestrator(
            IAiSettingsStore aiSettingsStore,
            IAiSecretsStore aiSecretsStore,
            IChatSessionStore chatSessionStore,
            ILlmUsageStatsStore llmUsageStatsStore,
            ModelFingerprintService modelFingerprintService,
            SoulPromptAssembler? soulPromptAssembler = null,
            SoulProfileStore? soulProfileStore = null,
            ReminderStore? reminderStore = null)
        {
            this.aiSettingsStore = aiSettingsStore ?? throw new ArgumentNullException(nameof(aiSettingsStore));
            this.aiSecretsStore = aiSecretsStore ?? throw new ArgumentNullException(nameof(aiSecretsStore));
            this.chatSessionStore = chatSessionStore ?? throw new ArgumentNullException(nameof(chatSessionStore));
            this.llmUsageStatsStore = llmUsageStatsStore ?? throw new ArgumentNullException(nameof(llmUsageStatsStore));
            this.modelFingerprintService = modelFingerprintService ?? throw new ArgumentNullException(nameof(modelFingerprintService));
            openAiCompatibleProvider = new OpenAiCompatibleLlmProvider();
            miniMaxProvider = new MiniMaxLlmProvider();
            this.soulProfileStore = soulProfileStore ?? new SoulProfileStore();
            this.reminderStore = reminderStore ?? new ReminderStore();
            this.soulPromptAssembler = soulPromptAssembler ?? new SoulPromptAssembler(this.soulProfileStore, this.reminderStore);
            memoryJudge = new MemoryJudge(openAiCompatibleProvider, miniMaxProvider, this.soulProfileStore);
        }

        public async Task<LlmResponseEnvelope> GenerateReplyAsync(
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            string supplementalInstruction = "",
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
            var session = PrepareSession(chatSessionStore.Load(sessionId, modelFingerprint));
            var userChatMessage = CreateUserMessage(session.SessionId, userMessage.Trim());
            var pendingThreadMessages = session.Messages.Concat(new[] { userChatMessage }).ToArray();
            var promptMessages = BuildPromptMessages(settings, pendingThreadMessages);
            var systemPrompt = soulPromptAssembler.BuildSystemPrompt(
                settings.GlobalSystemPrompt,
                characterDisplayName,
                modelFingerprint,
                supplementalInstruction);
            var requestContext = new LlmRequestContext(
                ProviderProfile: activeProvider,
                ApiKey: apiKey,
                SystemPrompt: systemPrompt,
                Temperature: settings.Temperature,
                MaxOutputTokens: settings.MaxOutputTokens,
                EnableStreaming: false,
                Messages: promptMessages);
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
                var savedSession = CompactSessionIfNeeded(
                    session with
                    {
                        Messages = pendingThreadMessages.Concat(new[] { assistantMessage }).ToArray(),
                        UpdatedAt = assistantMessage.CreatedAt,
                        LastUserMessageAt = userChatMessage.CreatedAt,
                    },
                    settings);
                chatSessionStore.Save(savedSession);
                llmUsageStatsStore.RecordSuccess(
                    response.ProviderId,
                    response.Model,
                    stopwatch.ElapsedMilliseconds,
                    response.PromptCharacters,
                    response.CompletionCharacters);
                QueueMemoryWrite(
                    activeProvider,
                    apiKey,
                    characterDisplayName,
                    modelFingerprint,
                    savedSession.ActiveThreadId,
                    userChatMessage.Text,
                    string.IsNullOrWhiteSpace(response.TtsText) ? assistantMessage.Text : response.TtsText);
                return response;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stopwatch.Stop();
                chatSessionStore.Save(session with
                {
                    Messages = pendingThreadMessages,
                    UpdatedAt = userChatMessage.CreatedAt,
                    LastUserMessageAt = userChatMessage.CreatedAt,
                });
                llmUsageStatsStore.RecordFailure(
                    activeProvider.Id,
                    activeProvider.Model,
                    exception.Message,
                    stopwatch.ElapsedMilliseconds,
                    promptMessages.Sum(static message => message.Text.Length));
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

        private ChatSessionData CompactSessionIfNeeded(ChatSessionData session, AiSettingsData settings)
        {
            var keepRecentMessageCount = Math.Max(4, settings.MemoryWindowTurns * 2);
            var compactThreshold = Math.Max(keepRecentMessageCount + 2, settings.SummaryThreshold);
            if (session.Messages.Count <= compactThreshold || session.Messages.Count <= keepRecentMessageCount)
            {
                return session;
            }

            var messagesToCompact = session.Messages
                .Take(session.Messages.Count - keepRecentMessageCount)
                .ToArray();
            if (messagesToCompact.Length == 0)
            {
                return session;
            }

            var compactId = BuildCompactId(session.ActiveThreadId, messagesToCompact);
            chatSessionStore.SaveCompact(new ChatCompactData(
                SessionId: session.SessionId,
                CharacterFingerprint: session.CharacterFingerprint,
                ThreadId: session.ActiveThreadId,
                CompactId: compactId,
                StartedAt: messagesToCompact.First().CreatedAt,
                EndedAt: messagesToCompact.Last().CreatedAt,
                MessageCount: messagesToCompact.Length,
                Content: BuildCompactContent(messagesToCompact)));
            return session with
            {
                Messages = session.Messages.Skip(messagesToCompact.Length).ToArray(),
            };
        }

        private static ChatMessage[] BuildPromptMessages(AiSettingsData settings, System.Collections.Generic.IEnumerable<ChatMessage> messages)
        {
            return TrimMessages(settings, messages);
        }

        private static ChatMessage[] TrimMessages(AiSettingsData settings, System.Collections.Generic.IEnumerable<ChatMessage> messages)
        {
            var maxMessages = Math.Max(4, settings.MemoryWindowTurns * 2);
            return messages
                .Where(static message => message != null)
                .TakeLast(maxMessages)
                .ToArray();
        }

        private static ChatSessionData PrepareSession(ChatSessionData session)
        {
            if (session.LastUserMessageAt == null || DateTimeOffset.UtcNow - session.LastUserMessageAt <= ThreadInactivityWindow)
            {
                return session;
            }

            return session with
            {
                ActiveThreadId = BuildThreadId(),
                Messages = Array.Empty<ChatMessage>(),
            };
        }

        private static ChatMessage CreateUserMessage(string sessionId, string userMessage)
        {
            return new ChatMessage(
                Id: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Role: ChatRole.User,
                Text: userMessage,
                CreatedAt: DateTimeOffset.UtcNow,
                Source: ChatInvocationSource.UserInput);
        }

        private static string BuildThreadId()
        {
            return $"thread-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        }

        private static string BuildCompactId(string threadId, IReadOnlyList<ChatMessage> messages)
        {
            return $"{threadId}-compact-{messages.Last().CreatedAt:yyyyMMddHHmmssfff}";
        }

        private static string BuildCompactContent(IReadOnlyList<ChatMessage> messages)
        {
            var startedAt = messages.First().CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            var endedAt = messages.Last().CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            var excerptLines = messages
                .Take(8)
                .Select(message => $"- {message.Role}: {BuildExcerpt(message.Text)}")
                .ToArray();
            var remainingMessageCount = Math.Max(0, messages.Count - excerptLines.Length);
            var remainingSection = remainingMessageCount > 0
                ? $"\n## Remaining Messages\n\n- {remainingMessageCount} 条更早消息已被收纳进该 compact。\n"
                : string.Empty;
            return string.Join(
                "\n",
                new[]
                {
                    "# Compact Summary",
                    string.Empty,
                    "## Time Range",
                    string.Empty,
                    $"- {startedAt} ~ {endedAt}",
                    string.Empty,
                    "## Message Count",
                    string.Empty,
                    $"- {messages.Count}",
                    string.Empty,
                    "## Transcript Excerpt",
                    string.Empty,
                    string.Join("\n", excerptLines),
                }) + remainingSection;
        }

        private static string BuildExcerpt(string value)
        {
            var normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return normalized.Length <= 120 ? normalized : $"{normalized[..120]}...";
        }

        private static string BuildSessionId(string characterFingerprint)
        {
            var normalized = string.IsNullOrWhiteSpace(characterFingerprint)
                ? "default-character"
                : characterFingerprint.Trim();
            return $"chat-{normalized}";
        }

        private async Task TryWriteMemoriesAsync(
            LlmProviderProfile activeProvider,
            string apiKey,
            string characterDisplayName,
            string characterFingerprint,
            string activeThreadId,
            string userMessage,
            string assistantReplyText,
            CancellationToken cancellationToken)
        {
            try
            {
                var writes = await memoryJudge.JudgeAsync(
                    activeProvider,
                    apiKey,
                    characterDisplayName,
                    characterFingerprint,
                    userMessage,
                    assistantReplyText,
                    activeThreadId,
                    cancellationToken);
                soulProfileStore.ApplyMemoryWrites(characterFingerprint, characterDisplayName, writes);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private void QueueMemoryWrite(
            LlmProviderProfile activeProvider,
            string apiKey,
            string characterDisplayName,
            string characterFingerprint,
            string activeThreadId,
            string userMessage,
            string assistantReplyText)
        {
            _ = TryWriteMemoriesAsync(
                activeProvider,
                apiKey,
                characterDisplayName,
                characterFingerprint,
                activeThreadId,
                userMessage,
                assistantReplyText,
                CancellationToken.None);
        }
    }
}
