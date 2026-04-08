#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VividSoul.Runtime.AI;
using VividSoul.Runtime.Content;

namespace VividSoul.Editor
{
    public static class LocalReminderE2ETools
    {
        private const string CharacterDisplayName = "ReminderE2E";
        private static readonly TimeSpan ProviderTurnTimeout = TimeSpan.FromSeconds(90);

        [MenuItem("VividSoul/Diagnostics/Run Local Reminder E2E")]
        public static void RunLocalReminderE2E()
        {
            var previousSynchronizationContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                RunLocalReminderE2EAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
            }
        }

        public static async Task RunLocalReminderE2EAsync()
        {
            var liveSettingsStore = new AiSettingsStore();
            var liveSecretsStore = new AiSecretsStore();
            var liveSettings = liveSettingsStore.Load();
            var activeProfile = ResolveActiveLocalProfile(liveSettings);
            var apiKey = liveSecretsStore.LoadApiKey(activeProfile.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException($"当前激活的本地 Provider `{activeProfile.DisplayName}` 没有可用 API Key，无法运行 reminder E2E。");
            }

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "VividSoulReminderE2E",
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var isolatedSettingsStore = new AiSettingsStore(tempRoot);
                var isolatedSecretsStore = new AiSecretsStore(tempRoot);
                var modelFingerprintService = new ModelFingerprintService();
                var reminderStore = new ReminderStore(tempRoot);
                var soulProfileStore = new SoulProfileStore(tempRoot);
                isolatedSettingsStore.Save(CloneSettingsForProfile(liveSettings, activeProfile));
                isolatedSecretsStore.SaveApiKey(activeProfile.Id, apiKey);

                var characterSourcePath = CreateDummyCharacterFile(tempRoot);
                var chatSessionStore = new ChatSessionStore(tempRoot);
                var usageStatsStore = new LlmUsageStatsStore(tempRoot);
                var soulPromptAssembler = new SoulPromptAssembler(soulProfileStore, reminderStore);
                var orchestrator = new MateConversationOrchestrator(
                    isolatedSettingsStore,
                    isolatedSecretsStore,
                    chatSessionStore,
                    usageStatsStore,
                    modelFingerprintService,
                    soulPromptAssembler,
                    soulProfileStore,
                    reminderStore);
                var reminderIntentJudge = new ReminderIntentJudge(new OpenAiCompatibleLlmProvider(), new MiniMaxLlmProvider(), reminderStore);
                Debug.Log($"[ReminderE2E] Using provider `{activeProfile.DisplayName}` ({activeProfile.ProviderType})");

                await RunCancelScenarioAsync(
                    activeProfile,
                    isolatedSecretsStore,
                    modelFingerprintService,
                    reminderStore,
                    orchestrator,
                    reminderIntentJudge,
                    characterSourcePath).ConfigureAwait(false);

                await RunCreateQueryAndTriggerScenarioAsync(
                    activeProfile,
                    isolatedSecretsStore,
                    modelFingerprintService,
                    reminderStore,
                    orchestrator,
                    reminderIntentJudge,
                    characterSourcePath).ConfigureAwait(false);

                await RunRestartRecoveryScenarioAsync(
                    activeProfile,
                    isolatedSecretsStore,
                    modelFingerprintService,
                    reminderStore,
                    orchestrator,
                    reminderIntentJudge,
                    characterSourcePath).ConfigureAwait(false);

                Debug.Log($"[ReminderE2E] All scenarios passed. tempRoot={tempRoot}");
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static async Task RunCancelScenarioAsync(
            LlmProviderProfile profile,
            IAiSecretsStore aiSecretsStore,
            ModelFingerprintService modelFingerprintService,
            ReminderStore reminderStore,
            MateConversationOrchestrator orchestrator,
            ReminderIntentJudge reminderIntentJudge,
            string characterSourcePath)
        {
            var cancelTitle = "喝水提醒测试";
            var dueAtLocal = DateTimeOffset.Now.AddMinutes(3);
            var createMessage = BuildAbsoluteReminderMessage(cancelTitle, dueAtLocal);
            var characterFingerprint = modelFingerprintService.ComputeSha256(characterSourcePath);
            Debug.Log($"[ReminderE2E] Cancel scenario create title={cancelTitle} due={dueAtLocal:O}");
            using var backend = CreateBackend(orchestrator, aiSecretsStore, modelFingerprintService, reminderIntentJudge, reminderStore);
            var envelopes = new List<ConversationMessageEnvelope>();
            backend.MessageReceived += envelopes.Add;
            await backend.ActivateAsync(profile, characterSourcePath, CharacterDisplayName, CancellationToken.None).ConfigureAwait(false);
            var apiKey = aiSecretsStore.LoadApiKey(profile.Id);
            var createDecision = await reminderIntentJudge.JudgeAsync(
                profile,
                apiKey,
                CharacterDisplayName,
                characterFingerprint,
                createMessage,
                "local-reminder-e2e-cancel",
                CancellationToken.None).ConfigureAwait(false);
            Debug.Log($"[ReminderE2E] Cancel scenario direct judge op={createDecision.Operation} title={createDecision.Title} dueAtUtc={createDecision.DueAtUtc:O} confidence={createDecision.Confidence:F2}");
            var createTurnCompleted = await SendUserMessageWithTimeoutAsync(
                backend,
                profile,
                characterSourcePath,
                CharacterDisplayName,
                createMessage).ConfigureAwait(false);
            Debug.Log($"[ReminderE2E] Cancel scenario create turn completed={createTurnCompleted}");
            Debug.Log($"[ReminderE2E] Cancel scenario reminders after create={DescribeReminders(reminderStore.LoadAll())}");
            var createdReminder = FindLatestReminder(reminderStore);
            if (createdReminder == null || createdReminder.Status != ReminderStatus.Pending)
            {
                throw new InvalidOperationException("取消场景：创建 reminder 后没有找到 pending 记录。");
            }

            Debug.Log("[ReminderE2E] Cancel scenario sending cancellation message");
            var cancelDecision = await reminderIntentJudge.JudgeAsync(
                profile,
                apiKey,
                CharacterDisplayName,
                characterFingerprint,
                "取消刚才那个提醒",
                "local-reminder-e2e-cancel",
                CancellationToken.None).ConfigureAwait(false);
            Debug.Log($"[ReminderE2E] Cancel scenario cancel judge op={cancelDecision.Operation} title={cancelDecision.Title} sourceText={cancelDecision.SourceText} confidence={cancelDecision.Confidence:F2}");
            var cancelTurnCompleted = await SendUserMessageWithTimeoutAsync(
                backend,
                profile,
                characterSourcePath,
                CharacterDisplayName,
                "取消刚才那个提醒").ConfigureAwait(false);
            Debug.Log($"[ReminderE2E] Cancel scenario cancel turn completed={cancelTurnCompleted}");
            Debug.Log($"[ReminderE2E] Cancel scenario reminders after cancel={DescribeReminders(reminderStore.LoadAll())}");
            var cancelledReminder = FindReminderById(reminderStore, createdReminder.Id);
            if (cancelledReminder == null || cancelledReminder.Status != ReminderStatus.Cancelled)
            {
                throw new InvalidOperationException("取消场景：提醒没有变成 cancelled。");
            }

            if (cancelTurnCompleted && !HasAssistantMessageContaining(envelopes, "取消"))
            {
                throw new InvalidOperationException("取消场景：主回复里没有看到取消确认。");
            }

            Debug.Log("[ReminderE2E] Cancel scenario passed.");
        }

        private static async Task RunCreateQueryAndTriggerScenarioAsync(
            LlmProviderProfile profile,
            IAiSecretsStore aiSecretsStore,
            ModelFingerprintService modelFingerprintService,
            ReminderStore reminderStore,
            MateConversationOrchestrator orchestrator,
            ReminderIntentJudge reminderIntentJudge,
            string characterSourcePath)
        {
            var triggerTitle = "番茄钟喝水测试";
            var dueAtLocal = DateTimeOffset.Now.AddSeconds(25);
            Debug.Log($"[ReminderE2E] Trigger scenario create title={triggerTitle} due={dueAtLocal:O}");
            using var backend = CreateBackend(orchestrator, aiSecretsStore, modelFingerprintService, reminderIntentJudge, reminderStore);
            var envelopes = new List<ConversationMessageEnvelope>();
            backend.MessageReceived += envelopes.Add;
            await backend.ActivateAsync(profile, characterSourcePath, CharacterDisplayName, CancellationToken.None).ConfigureAwait(false);
            var createTurnCompleted = await SendUserMessageWithTimeoutAsync(
                backend,
                profile,
                characterSourcePath,
                CharacterDisplayName,
                BuildAbsoluteReminderMessage(triggerTitle, dueAtLocal)).ConfigureAwait(false);
            Debug.Log($"[ReminderE2E] Trigger scenario create turn completed={createTurnCompleted}");

            var createdReminder = FindLatestReminder(reminderStore);
            if (createdReminder == null || createdReminder.Status != ReminderStatus.Pending)
            {
                throw new InvalidOperationException("创建/触发场景：创建 reminder 后没有找到 pending 记录。");
            }

            var assistantCountBeforeQuery = envelopes.Count;
            Debug.Log("[ReminderE2E] Trigger scenario querying pending reminder");
            var queryTurnCompleted = await SendUserMessageWithTimeoutAsync(
                backend,
                profile,
                characterSourcePath,
                CharacterDisplayName,
                "我让你提醒了什么来着？").ConfigureAwait(false);
            if (!queryTurnCompleted)
            {
                throw new InvalidOperationException("创建/触发场景：查询 reminder 时主回复超时。");
            }
            var queryReply = envelopes.Skip(assistantCountBeforeQuery)
                .Where(static envelope => envelope.Message.Role == ChatRole.Assistant && !envelope.IsProactive)
                .Select(envelope => envelope.Message.Text)
                .LastOrDefault() ?? string.Empty;
            if (!ContainsReminderReference(queryReply, createdReminder))
            {
                throw new InvalidOperationException($"创建/触发场景：查询待提醒内容时没有提到 reminder。实际回复：{queryReply}");
            }

            var delivered = await WaitForProactiveMessageAsync(
                backend,
                envelopes,
                createdReminder.Title,
                TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            if (!delivered)
            {
                throw new InvalidOperationException("创建/触发场景：等待到点后没有收到 proactive reminder。");
            }

            var deliveredReminder = FindReminderById(reminderStore, createdReminder.Id);
            if (deliveredReminder == null || deliveredReminder.Status != ReminderStatus.Delivered)
            {
                throw new InvalidOperationException("创建/触发场景：到点后 reminder 没有变成 delivered。");
            }

            Debug.Log("[ReminderE2E] Create/query/trigger scenario passed.");
        }

        private static async Task RunRestartRecoveryScenarioAsync(
            LlmProviderProfile profile,
            IAiSecretsStore aiSecretsStore,
            ModelFingerprintService modelFingerprintService,
            ReminderStore reminderStore,
            MateConversationOrchestrator orchestrator,
            ReminderIntentJudge reminderIntentJudge,
            string characterSourcePath)
        {
            var restartTitle = "重启补触发测试";
            var dueAtLocal = DateTimeOffset.Now.AddSeconds(8);
            Debug.Log($"[ReminderE2E] Restart scenario create title={restartTitle} due={dueAtLocal:O}");
            var restartReminderId = string.Empty;
            List<ConversationMessageEnvelope> firstRunEnvelopes = new();
            using (var firstBackend = CreateBackend(orchestrator, aiSecretsStore, modelFingerprintService, reminderIntentJudge, reminderStore))
            {
                firstBackend.MessageReceived += firstRunEnvelopes.Add;
                await firstBackend.ActivateAsync(profile, characterSourcePath, CharacterDisplayName, CancellationToken.None).ConfigureAwait(false);
                var createTurnCompleted = await SendUserMessageWithTimeoutAsync(
                    firstBackend,
                    profile,
                    characterSourcePath,
                    CharacterDisplayName,
                    BuildAbsoluteReminderMessage(restartTitle, dueAtLocal)).ConfigureAwait(false);
                Debug.Log($"[ReminderE2E] Restart scenario create turn completed={createTurnCompleted}");
                var createdReminder = FindLatestReminder(reminderStore);
                if (createdReminder == null || createdReminder.Status != ReminderStatus.Pending)
                {
                    throw new InvalidOperationException("重启补触发场景：创建 reminder 后没有找到 pending 记录。");
                }
                restartReminderId = createdReminder.Id;

                await firstBackend.DeactivateAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Thread.Sleep(TimeSpan.FromSeconds(10));
            Debug.Log("[ReminderE2E] Restart scenario reactivating backend after due time");

            using var secondBackend = CreateBackend(orchestrator, aiSecretsStore, modelFingerprintService, reminderIntentJudge, reminderStore);
            var secondRunEnvelopes = new List<ConversationMessageEnvelope>();
            secondBackend.MessageReceived += secondRunEnvelopes.Add;
            await secondBackend.ActivateAsync(profile, characterSourcePath, CharacterDisplayName, CancellationToken.None).ConfigureAwait(false);
            var recovered = await WaitForProactiveMessageAsync(
                secondBackend,
                secondRunEnvelopes,
                FindReminderById(reminderStore, restartReminderId)?.Title ?? restartTitle,
                TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            if (!recovered)
            {
                throw new InvalidOperationException("重启补触发场景：重新激活 backend 后没有补触发 due reminder。");
            }

            var deliveredReminder = FindReminderById(reminderStore, restartReminderId);
            if (deliveredReminder == null || deliveredReminder.Status != ReminderStatus.Delivered)
            {
                throw new InvalidOperationException("重启补触发场景：补触发后 reminder 没有变成 delivered。");
            }

            Debug.Log("[ReminderE2E] Restart recovery scenario passed.");
        }

        private static async Task<bool> WaitForProactiveMessageAsync(
            LocalLlmConversationBackend backend,
            IReadOnlyList<ConversationMessageEnvelope> envelopes,
            string expectedTitle,
            TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            var unscaledTime = 0f;
            while (DateTimeOffset.UtcNow < deadline)
            {
                backend.Tick(unscaledTime);
                if (envelopes.Any(envelope =>
                        envelope.IsProactive
                        && envelope.Message.Role == ChatRole.Assistant
                        && envelope.Message.Text.Contains(expectedTitle, StringComparison.Ordinal)))
                {
                    return true;
                }

                await Task.Delay(500).ConfigureAwait(false);
                unscaledTime += 0.5f;
            }

            backend.Tick(unscaledTime + 1f);
            return envelopes.Any(envelope =>
                envelope.IsProactive
                && envelope.Message.Role == ChatRole.Assistant
                && envelope.Message.Text.Contains(expectedTitle, StringComparison.Ordinal));
        }

        private static async Task<bool> SendUserMessageWithTimeoutAsync(
            LocalLlmConversationBackend backend,
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            string userMessage)
        {
            using var cancellationTokenSource = new CancellationTokenSource(ProviderTurnTimeout);
            Debug.Log($"[ReminderE2E] Sending: {userMessage}");
            try
            {
                await backend.SendUserMessageAsync(
                    profile,
                    characterSourcePath,
                    characterDisplayName,
                    userMessage,
                    cancellationTokenSource.Token).ConfigureAwait(false);
                Debug.Log("[ReminderE2E] Send completed");
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[ReminderE2E] Send timed out before main reply completed");
                return false;
            }
        }

        private static LocalLlmConversationBackend CreateBackend(
            MateConversationOrchestrator orchestrator,
            IAiSecretsStore aiSecretsStore,
            ModelFingerprintService modelFingerprintService,
            ReminderIntentJudge reminderIntentJudge,
            ReminderStore reminderStore)
        {
            return new LocalLlmConversationBackend(
                orchestrator,
                aiSecretsStore,
                modelFingerprintService,
                reminderIntentJudge,
                null,
                reminderStore);
        }

        private static ReminderRecord? FindLatestReminder(ReminderStore reminderStore)
        {
            return reminderStore.LoadAll()
                .OrderByDescending(reminder => reminder.CreatedAtUtc)
                .FirstOrDefault();
        }

        private static ReminderRecord? FindReminderById(ReminderStore reminderStore, string reminderId)
        {
            return reminderStore.LoadAll()
                .FirstOrDefault(reminder => string.Equals(reminder.Id, reminderId, StringComparison.Ordinal));
        }

        private static bool ContainsReminderReference(string reply, ReminderRecord reminder)
        {
            if (string.IsNullOrWhiteSpace(reply) || reminder == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(reminder.Title)
                && reply.Contains(reminder.Title, StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(reminder.Note)
                && reply.Contains(reminder.Note, StringComparison.Ordinal);
        }

        private static string DescribeReminders(IReadOnlyList<ReminderRecord> reminders)
        {
            if (reminders == null || reminders.Count == 0)
            {
                return "<empty>";
            }

            return string.Join(
                " || ",
                reminders.Select(reminder =>
                    $"id={reminder.Id},title={reminder.Title},status={reminder.Status},thread={reminder.SourceThreadId},due={reminder.DueAtUtc:O}"));
        }

        private static bool HasAssistantMessageContaining(IReadOnlyList<ConversationMessageEnvelope> envelopes, string text)
        {
            return envelopes.Any(envelope =>
                envelope.Message.Role == ChatRole.Assistant
                && !envelope.IsProactive
                && envelope.Message.Text.Contains(text, StringComparison.Ordinal));
        }

        private static AiSettingsData CloneSettingsForProfile(AiSettingsData settings, LlmProviderProfile profile)
        {
            return settings with
            {
                ActiveProviderId = profile.Id,
                ProviderProfiles = new[] { profile },
            };
        }

        private static LlmProviderProfile ResolveActiveLocalProfile(AiSettingsData settings)
        {
            var activeProfile = settings.ProviderProfiles.FirstOrDefault(profile =>
                                    string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                                ?? settings.ProviderProfiles.FirstOrDefault();
            if (activeProfile == null)
            {
                throw new InvalidOperationException("当前没有可用 Provider，无法运行 reminder E2E。");
            }

            if (activeProfile.ProviderType == LlmProviderType.OpenClaw)
            {
                throw new InvalidOperationException("当前激活的是 OpenClaw，不是本地 provider 路径，无法运行本轮 local reminder E2E。");
            }

            if (!activeProfile.Enabled)
            {
                throw new InvalidOperationException("当前激活的本地 Provider 处于禁用状态。");
            }

            return activeProfile;
        }

        private static string CreateDummyCharacterFile(string tempRoot)
        {
            var characterPath = Path.Combine(tempRoot, "ReminderE2ECharacter.vrm");
            File.WriteAllText(characterPath, "ReminderE2ECharacter");
            return characterPath;
        }

        private static string BuildAbsoluteReminderMessage(string title, DateTimeOffset dueAtLocal)
        {
            return $"请在今天{dueAtLocal:HH:mm:ss}提醒我{title}。";
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
