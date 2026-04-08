#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime.Content;

namespace VividSoul.Runtime.AI
{
    public sealed class LocalLlmConversationBackend : IMateConversationBackend
    {
        private static readonly TimeSpan ActionIntentTimeout = TimeSpan.FromSeconds(2);
        private readonly MateConversationOrchestrator mateConversationOrchestrator;
        private readonly IAiSecretsStore aiSecretsStore;
        private readonly ModelFingerprintService modelFingerprintService;
        private readonly ReminderIntentJudge reminderIntentJudge;
        private readonly ActionIntentJudge actionIntentJudge;
        private readonly ReminderStore reminderStore;
        private readonly ReminderScheduler reminderScheduler;
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
        private LlmProviderProfile? activeProfile;
        private string activeCharacterSourcePath = string.Empty;
        private string activeCharacterDisplayName = string.Empty;
        private string activeCharacterFingerprint = string.Empty;
        private int unreadCount;

        public LocalLlmConversationBackend(
            MateConversationOrchestrator mateConversationOrchestrator,
            IAiSecretsStore aiSecretsStore,
            ModelFingerprintService modelFingerprintService,
            ReminderIntentJudge? reminderIntentJudge = null,
            ActionIntentJudge? actionIntentJudge = null,
            ReminderStore? reminderStore = null)
        {
            this.mateConversationOrchestrator = mateConversationOrchestrator ?? throw new ArgumentNullException(nameof(mateConversationOrchestrator));
            this.aiSecretsStore = aiSecretsStore ?? throw new ArgumentNullException(nameof(aiSecretsStore));
            this.modelFingerprintService = modelFingerprintService ?? throw new ArgumentNullException(nameof(modelFingerprintService));
            this.reminderStore = reminderStore ?? new ReminderStore();
            this.reminderIntentJudge = reminderIntentJudge ?? new ReminderIntentJudge(new OpenAiCompatibleLlmProvider(), new MiniMaxLlmProvider(), this.reminderStore);
            this.actionIntentJudge = actionIntentJudge ?? new ActionIntentJudge(new OpenAiCompatibleLlmProvider(), new MiniMaxLlmProvider());
            reminderScheduler = new ReminderScheduler(this.reminderStore, HandleReminderDelivered);
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
            cancellationToken.ThrowIfCancellationRequested();
            activeProfile = profile;
            activeCharacterSourcePath = characterSourcePath?.Trim() ?? string.Empty;
            activeCharacterDisplayName = characterDisplayName?.Trim() ?? string.Empty;
            activeCharacterFingerprint = string.IsNullOrWhiteSpace(activeCharacterSourcePath)
                ? string.Empty
                : modelFingerprintService.ComputeSha256(activeCharacterSourcePath);
            reminderScheduler.Activate();
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
            activeProfile = profile;
            activeCharacterSourcePath = characterSourcePath?.Trim() ?? string.Empty;
            activeCharacterDisplayName = characterDisplayName?.Trim() ?? string.Empty;
            activeCharacterFingerprint = string.IsNullOrWhiteSpace(activeCharacterSourcePath)
                ? string.Empty
                : modelFingerprintService.ComputeSha256(activeCharacterSourcePath);
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
                var reminderInstruction = await TryHandleReminderIntentAsync(
                    profile,
                    characterSourcePath,
                    characterDisplayName,
                    userMessage,
                    cancellationToken).ConfigureAwait(false);
                var response = await mateConversationOrchestrator.GenerateReplyAsync(
                    characterSourcePath,
                    characterDisplayName,
                    userMessage,
                    reminderInstruction,
                    cancellationToken).ConfigureAwait(false);
                var actionRequest = await TryResolveConversationActionAsync(
                    profile,
                    characterDisplayName,
                    userMessage,
                    response.TtsText,
                    BuildLocalSessionId(characterSourcePath),
                    cancellationToken).ConfigureAwait(false);
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
                    IsOptimistic: false,
                    ActionRequest: actionRequest));
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

            reminderScheduler.Deactivate();
            activeProfile = null;
            activeCharacterSourcePath = string.Empty;
            activeCharacterDisplayName = string.Empty;
            activeCharacterFingerprint = string.Empty;
            unreadCount = 0;
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
            unreadCount = 0;
            if (activeProfile == null)
            {
                return;
            }

            PublishStatus(activeProfile, false);
        }

        public void Tick(float unscaledTime)
        {
            reminderScheduler.Tick(unscaledTime);
        }

        public void NotifyApplicationFocus(bool hasFocus)
        {
            reminderScheduler.NotifyApplicationFocus(hasFocus);
        }

        public void Dispose()
        {
            reminderScheduler.Deactivate();
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
                UnreadCount: unreadCount);
            StatusChanged?.Invoke(lastStatus);
        }

        private async Task<string> TryHandleReminderIntentAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken)
        {
            try
            {
                var apiKey = aiSecretsStore.LoadApiKey(profile.Id);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return string.Empty;
                }

                var sourceThreadId = BuildLocalSessionId(characterSourcePath);
                var decision = await reminderIntentJudge.JudgeAsync(
                    profile,
                    apiKey,
                    characterDisplayName,
                    activeCharacterFingerprint,
                    userMessage,
                    sourceThreadId,
                    cancellationToken).ConfigureAwait(false);
                return ApplyReminderDecision(decision, sourceThreadId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ApplyReminderDecision(ReminderIntentDecision decision, string sourceThreadId)
        {
            if (string.Equals(decision.Operation, "create", StringComparison.Ordinal) && decision.DueAtUtc != null)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                if (decision.DueAtUtc.Value < nowUtc.AddMinutes(-2))
                {
                    return "The latest user message likely asks for a reminder, but the resolved time is too far in the past to schedule safely. Ask one brief clarification question in Chinese.";
                }

                var createdReminder = reminderStore.CreateOrGetExisting(new ReminderRecord(
                    Id: BuildReminderId(),
                    Title: decision.Title,
                    Note: string.IsNullOrWhiteSpace(decision.Note) ? $"提醒用户：{decision.Title}" : decision.Note,
                    DueAtUtc: decision.DueAtUtc.Value < nowUtc ? nowUtc : decision.DueAtUtc.Value.ToUniversalTime(),
                    Timezone: ResolveLocalTimezoneId(),
                    Status: ReminderStatus.Pending,
                    CreatedAtUtc: nowUtc,
                    UpdatedAtUtc: nowUtc,
                    CharacterFingerprint: activeCharacterFingerprint,
                    SourceThreadId: sourceThreadId,
                    DeliveredAtUtc: null,
                    AcknowledgedAtUtc: null));
                reminderScheduler.RequestImmediateScan();
                return string.Join(
                    "\n",
                    "A local reminder has already been created successfully before this reply.",
                    $"Reminder title: {createdReminder.Title}",
                    $"Reminder dueAtUtc: {createdReminder.DueAtUtc:O}",
                    $"Reminder timezone: {createdReminder.Timezone}",
                    "Reply in natural Chinese and briefly confirm the reminder was created. Do not output JSON.");
            }

            if (string.Equals(decision.Operation, "cancel", StringComparison.Ordinal))
            {
                var cancelledReminder = reminderStore.TryCancelPending(decision.Title, decision.SourceText, activeCharacterFingerprint);
                return cancelledReminder != null
                    ? $"A pending reminder was cancelled successfully before this reply. Reminder title: {cancelledReminder.Title}. Briefly confirm the cancellation in natural Chinese."
                    : "The user is trying to cancel a reminder, but no matching pending reminder was found. Explain this briefly in natural Chinese and ask whether they want to recreate it if needed.";
            }

            if (string.Equals(decision.Operation, "complete", StringComparison.Ordinal))
            {
                var completedReminder = reminderStore.TryCompletePending(decision.Title, decision.SourceText, activeCharacterFingerprint);
                return completedReminder != null
                    ? $"A pending reminder was marked completed before this reply. Reminder title: {completedReminder.Title}. Briefly acknowledge completion in natural Chinese."
                    : "The user is referring to finishing a reminder, but no matching pending reminder was found. Explain this briefly in natural Chinese.";
            }

            if (string.Equals(decision.Operation, "needsClarification", StringComparison.Ordinal))
            {
                return $"The latest user message likely asks for a reminder, but the time/details are not clear enough to schedule reliably. Ask one brief clarification question in Chinese. Reason: {decision.Note}";
            }

            return string.Empty;
        }

        private void HandleReminderDelivered(ReminderRecord reminder)
        {
            unreadCount++;
            MessageReceived?.Invoke(new ConversationMessageEnvelope(
                Message: new ChatMessage(
                    Id: Guid.NewGuid().ToString("N"),
                    SessionId: string.IsNullOrWhiteSpace(reminder.SourceThreadId) ? "local-reminder" : reminder.SourceThreadId,
                    Role: ChatRole.Assistant,
                    Text: BuildReminderDeliveryText(reminder),
                    CreatedAt: DateTimeOffset.UtcNow,
                    Source: ChatInvocationSource.ProactiveTick),
                IsProactive: true,
                ShouldDisplayBubble: true,
                ShouldSpeak: false,
                IsOptimistic: false));
            if (activeProfile != null)
            {
                PublishStatus(activeProfile, false);
            }
        }

        private static string BuildReminderDeliveryText(ReminderRecord reminder)
        {
            var title = string.IsNullOrWhiteSpace(reminder.Title) ? "之前约好的提醒" : reminder.Title.Trim();
            return $"提醒一下，{title} 到时间了。";
        }

        private async Task<ConversationActionRequest?> TryResolveConversationActionAsync(
            LlmProviderProfile profile,
            string characterDisplayName,
            string userMessage,
            string assistantMessage,
            string sourceThreadId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assistantMessage))
            {
                return null;
            }

            try
            {
                var apiKey = aiSecretsStore.LoadApiKey(profile.Id);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return null;
                }

                using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCancellationTokenSource.CancelAfter(ActionIntentTimeout);
                var decision = await actionIntentJudge.JudgeAsync(
                    profile,
                    apiKey,
                    characterDisplayName,
                    userMessage,
                    assistantMessage,
                    sourceThreadId,
                    timeoutCancellationTokenSource.Token).ConfigureAwait(false);
                return string.Equals(decision.Operation, "playBuiltInPose", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(decision.ActionId)
                    ? new ConversationActionRequest(ConversationActionKind.PlayBuiltInPose, decision.ActionId)
                    : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveLocalTimezoneId()
        {
            try
            {
                return TimeZoneInfo.Local.Id;
            }
            catch
            {
                return "UTC";
            }
        }

        private static string BuildReminderId()
        {
            return $"rem-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
        }

        private static string BuildLocalSessionId(string characterSourcePath)
        {
            return string.IsNullOrWhiteSpace(characterSourcePath)
                ? "local-default-session"
                : $"local:{characterSourcePath.GetHashCode():X8}";
        }
    }
}
