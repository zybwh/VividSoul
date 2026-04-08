#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.AI
{
    public sealed class MemoryJudge
    {
        private readonly ILlmProvider openAiCompatibleProvider;
        private readonly ILlmProvider miniMaxProvider;
        private readonly SoulProfileStore soulProfileStore;

        public MemoryJudge(
            ILlmProvider openAiCompatibleProvider,
            ILlmProvider miniMaxProvider,
            SoulProfileStore soulProfileStore)
        {
            this.openAiCompatibleProvider = openAiCompatibleProvider ?? throw new ArgumentNullException(nameof(openAiCompatibleProvider));
            this.miniMaxProvider = miniMaxProvider ?? throw new ArgumentNullException(nameof(miniMaxProvider));
            this.soulProfileStore = soulProfileStore ?? throw new ArgumentNullException(nameof(soulProfileStore));
        }

        public async Task<IReadOnlyList<MemoryWriteEntry>> JudgeAsync(
            LlmProviderProfile providerProfile,
            string apiKey,
            string characterDisplayName,
            string characterFingerprint,
            string userMessage,
            string assistantMessage,
            string sourceThreadId,
            CancellationToken cancellationToken)
        {
            if (providerProfile == null)
            {
                throw new ArgumentNullException(nameof(providerProfile));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("An API key is required.", nameof(apiKey));
            }

            var requestContext = new LlmRequestContext(
                ProviderProfile: providerProfile,
                ApiKey: apiKey.Trim(),
                SystemPrompt: BuildSystemPrompt(characterDisplayName, characterFingerprint),
                Temperature: 0.1f,
                MaxOutputTokens: 220,
                EnableStreaming: false,
                Messages: new[]
                {
                    new ChatMessage(
                        Id: Guid.NewGuid().ToString("N"),
                        SessionId: sourceThreadId,
                        Role: ChatRole.User,
                        Text: BuildJudgeInput(userMessage, assistantMessage, sourceThreadId),
                        CreatedAt: DateTimeOffset.UtcNow,
                        Source: ChatInvocationSource.System),
                });
            var provider = ResolveProvider(providerProfile.ProviderType);
            var response = await provider.GenerateAsync(requestContext, cancellationToken);
            return ParseWrites(response.TtsText, sourceThreadId);
        }

        private string BuildSystemPrompt(string characterDisplayName, string characterFingerprint)
        {
            var normalizedDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? "VividSoul Mate"
                : characterDisplayName.Trim();
            return string.Join(
                "\n\n",
                new[]
                {
                    "You are a memory judge for a desktop companion system.",
                    "Return JSON only. No markdown. No prose. No code fences.",
                    "Decide whether the latest exchange contains durable memory worth storing.",
                    "Use only these memoryType values: explicitOverride, explicitPreference, stableFact, inferredHabitCandidate, bondUpdate, openCommitment, noWrite.",
                    "Use only these scope values: user, character, bond.",
                    "Favor precision over recall. If unsure, return an empty writes array.",
                    "Only use explicitOverride when the user clearly corrects or replaces an old preference or fact.",
                    "Only use inferredHabitCandidate when a cautious habit hypothesis is justified; do not over-infer from one casual line.",
                    "Each write must be a short Chinese sentence without markdown.",
                    "Return this shape exactly: {\"writes\":[{\"memoryType\":\"explicitPreference\",\"scope\":\"user\",\"text\":\"用户偏好简短回复\",\"priority\":\"high\",\"replaces\":\"\",\"sourceThreadId\":\"thread-...\",\"confidence\":0.95}]}",
                    $"Current character: {normalizedDisplayName}.",
                    $"ROLE:\n{soulProfileStore.LoadRoleMarkdown(characterFingerprint, characterDisplayName)}",
                    $"USER_HABITS:\n{soulProfileStore.LoadHabitsMarkdown()}",
                    $"USER_FACTS:\n{soulProfileStore.LoadUserFactsMarkdown()}",
                    $"BOND:\n{soulProfileStore.LoadBondMarkdown(characterFingerprint, characterDisplayName)}",
                    $"CHARACTER_FACTS:\n{soulProfileStore.LoadCharacterFactsMarkdown(characterFingerprint, characterDisplayName)}",
                });
        }

        private static string BuildJudgeInput(string userMessage, string assistantMessage, string sourceThreadId)
        {
            return string.Join(
                "\n",
                new[]
                {
                    $"sourceThreadId: {sourceThreadId}",
                    $"latestUserMessage: {userMessage.Trim()}",
                    $"latestAssistantReply: {assistantMessage.Trim()}",
                    "Analyze only this latest exchange and the provided memory context.",
                });
        }

        private ILlmProvider ResolveProvider(LlmProviderType providerType)
        {
            return providerType switch
            {
                LlmProviderType.MiniMax => miniMaxProvider,
                _ => openAiCompatibleProvider,
            };
        }

        private static IReadOnlyList<MemoryWriteEntry> ParseWrites(string rawJson, string sourceThreadId)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Array.Empty<MemoryWriteEntry>();
            }

            if (MiniJson.Deserialize(rawJson) is not Dictionary<string, object?> root
                || !root.TryGetValue("writes", out var writesValue)
                || writesValue is not List<object?> writesList)
            {
                return Array.Empty<MemoryWriteEntry>();
            }

            return writesList
                .OfType<Dictionary<string, object?>>()
                .Select(write => ParseWrite(write, sourceThreadId))
                .Where(static write => write != null)
                .Cast<MemoryWriteEntry>()
                .Where(static write => !string.Equals(write.MemoryType, "noWrite", StringComparison.OrdinalIgnoreCase))
                .Where(static write => !string.IsNullOrWhiteSpace(write.Text))
                .Take(4)
                .ToArray();
        }

        private static MemoryWriteEntry? ParseWrite(IReadOnlyDictionary<string, object?> payload, string sourceThreadId)
        {
            var memoryType = ReadString(payload, "memoryType");
            var scope = ReadString(payload, "scope");
            var text = ReadString(payload, "text");
            if (string.IsNullOrWhiteSpace(memoryType) || string.IsNullOrWhiteSpace(scope))
            {
                return null;
            }

            return new MemoryWriteEntry(
                MemoryType: memoryType,
                Scope: scope,
                Text: text,
                Priority: ReadString(payload, "priority"),
                Replaces: ReadString(payload, "replaces"),
                SourceThreadId: string.IsNullOrWhiteSpace(ReadString(payload, "sourceThreadId")) ? sourceThreadId : ReadString(payload, "sourceThreadId"),
                Confidence: ReadDouble(payload, "confidence"));
        }

        private static string ReadString(IReadOnlyDictionary<string, object?> payload, string key)
        {
            return payload.TryGetValue(key, out var value) && value is string text
                ? text.Trim()
                : string.Empty;
        }

        private static double ReadDouble(IReadOnlyDictionary<string, object?> payload, string key)
        {
            if (!payload.TryGetValue(key, out var value) || value == null)
            {
                return 0d;
            }

            return value switch
            {
                double number => number,
                float number => number,
                long number => number,
                int number => number,
                _ when double.TryParse(value.ToString(), out var parsedValue) => parsedValue,
                _ => 0d,
            };
        }
    }

    public sealed record MemoryWriteEntry(
        string MemoryType,
        string Scope,
        string Text,
        string Priority,
        string Replaces,
        string SourceThreadId,
        double Confidence);
}
