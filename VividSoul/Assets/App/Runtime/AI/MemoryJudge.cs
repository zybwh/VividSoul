#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime;

namespace VividSoul.Runtime.AI
{
    public sealed class MemoryJudge
    {
        private const string MemoryToolName = "submit_memory_writes";
        private const double ExplicitOverrideConfidenceThreshold = 0.72d;
        private const double ExplicitPreferenceConfidenceThreshold = 0.78d;
        private const double StableFactConfidenceThreshold = 0.82d;
        private const double InferredHabitCandidateConfidenceThreshold = 0.60d;
        private const double BondUpdateConfidenceThreshold = 0.74d;
        private const double OpenCommitmentConfidenceThreshold = 0.74d;
        private const int JudgeMaxOutputTokens = 640;
        private const int RoleContextMaxCharacters = 700;
        private const int HabitsContextMaxCharacters = 500;
        private const int UserFactsContextMaxCharacters = 500;
        private const int BondContextMaxCharacters = 500;
        private const int CharacterFactsContextMaxCharacters = 500;
        private static readonly LlmToolDefinition MemoryWriteTool = new(
            Name: MemoryToolName,
            Description: "Submit the durable memory writes extracted from the latest exchange.",
            ParametersSchema: BuildMemoryToolSchema());
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
                MaxOutputTokens: JudgeMaxOutputTokens,
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
                },
                Tools: new[] { MemoryWriteTool },
                ForcedToolName: MemoryToolName);
            var provider = ResolveProvider(providerProfile.ProviderType);
            var response = await provider.GenerateAsync(requestContext, cancellationToken);
            return ParseWrites(response, sourceThreadId);
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
                    "Extract only durable memory from the latest exchange.",
                    "Think briefly and call the tool as soon as the write set is clear.",
                    "Most exchanges should produce no write.",
                    "Store only durable cross-session preferences, facts, relationship updates, or open commitments.",
                    "Ignore one-off requests, temporary moods, temporary plans, and generic small talk.",
                    "Use explicitOverride only when the user clearly corrects or replaces an older preference or fact.",
                    "Set replaces only when the older text is clearly visible in the provided memory context; otherwise leave it empty.",
                    "Use inferredHabitCandidate only for cautious low-risk habit hypotheses.",
                    "Each text field should be a short Chinese plain-text sentence without markdown.",
                    "Call the tool exactly once.",
                    "Do not explain the extraction before the tool call.",
                    $"Current character: {normalizedDisplayName}.",
                    BuildContextSection("ROLE", soulProfileStore.LoadRoleMarkdown(characterFingerprint, characterDisplayName), RoleContextMaxCharacters),
                    BuildContextSection("USER_HABITS", soulProfileStore.LoadHabitsMarkdown(), HabitsContextMaxCharacters),
                    BuildContextSection("USER_FACTS", soulProfileStore.LoadUserFactsMarkdown(), UserFactsContextMaxCharacters),
                    BuildContextSection("BOND", soulProfileStore.LoadBondMarkdown(characterFingerprint, characterDisplayName), BondContextMaxCharacters),
                    BuildContextSection("CHARACTER_FACTS", soulProfileStore.LoadCharacterFactsMarkdown(characterFingerprint, characterDisplayName), CharacterFactsContextMaxCharacters),
                    "Do not answer with prose. Use the tool.",
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

        private static string BuildContextSection(string title, string content, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return $"[{title}]\n<empty>";
            }

            var normalizedContent = content.Trim();
            if (normalizedContent.Length > maxCharacters)
            {
                normalizedContent = $"{normalizedContent[..maxCharacters].TrimEnd()}\n...[truncated]";
            }

            return $"[{title}]\n{normalizedContent}";
        }

        private ILlmProvider ResolveProvider(LlmProviderType providerType)
        {
            return providerType switch
            {
                LlmProviderType.MiniMax => miniMaxProvider,
                _ => openAiCompatibleProvider,
            };
        }

        private static IReadOnlyList<MemoryWriteEntry> ParseWrites(LlmResponseEnvelope response, string sourceThreadId)
        {
            var root = ParseToolPayload(response);
            if (root == null
                && !string.IsNullOrWhiteSpace(response.RawText)
                && MiniJson.Deserialize(response.RawText) is Dictionary<string, object?> fallbackRoot)
            {
                root = fallbackRoot;
            }

            if (root == null)
            {
                return Array.Empty<MemoryWriteEntry>();
            }

            if (!root.TryGetValue("writes", out var writesValue)
                || writesValue is not List<object?> writesList)
            {
                return Array.Empty<MemoryWriteEntry>();
            }

            return writesList
                .OfType<Dictionary<string, object?>>()
                .Select(write => ParseWrite(write, sourceThreadId))
                .Where(static write => write != null)
                .Cast<MemoryWriteEntry>()
                .Where(ShouldPersistWrite)
                .GroupBy(write => BuildDeduplicationKey(write), StringComparer.Ordinal)
                .Select(SelectBestWrite)
                .OrderByDescending(GetWritePriorityScore)
                .ThenByDescending(write => write.Confidence)
                .Take(4)
                .ToArray();
        }

        private static MemoryWriteEntry? ParseWrite(IReadOnlyDictionary<string, object?> payload, string sourceThreadId)
        {
            var memoryType = NormalizeMemoryType(ReadString(payload, "memoryType"));
            var scope = NormalizeScope(ReadString(payload, "scope"));
            var text = NormalizeWriteText(ReadString(payload, "text"));
            if (string.IsNullOrWhiteSpace(memoryType)
                || string.IsNullOrWhiteSpace(scope)
                || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new MemoryWriteEntry(
                MemoryType: memoryType,
                Scope: scope,
                Text: text,
                Priority: NormalizePriority(ReadString(payload, "priority"), memoryType),
                Replaces: NormalizeWriteText(ReadString(payload, "replaces")),
                SourceThreadId: sourceThreadId,
                Confidence: ClampConfidence(ReadDouble(payload, "confidence")));
        }

        private static bool ShouldPersistWrite(MemoryWriteEntry write)
        {
            if (write == null
                || string.IsNullOrWhiteSpace(write.MemoryType)
                || string.IsNullOrWhiteSpace(write.Scope)
                || string.IsNullOrWhiteSpace(write.Text)
                || string.Equals(write.MemoryType, "noWrite", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsSupportedScope(write.MemoryType, write.Scope))
            {
                return false;
            }

            if (BuildComparisonValue(write.Text).Length < 3)
            {
                return false;
            }

            return write.Confidence >= GetMinimumConfidence(write.MemoryType);
        }

        private static bool IsSupportedScope(string memoryType, string scope)
        {
            return memoryType switch
            {
                "explicitPreference" => string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase),
                "explicitOverride" => string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(scope, "character", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(scope, "bond", StringComparison.OrdinalIgnoreCase),
                "stableFact" => string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(scope, "character", StringComparison.OrdinalIgnoreCase),
                "inferredHabitCandidate" => string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase),
                "bondUpdate" => string.Equals(scope, "bond", StringComparison.OrdinalIgnoreCase),
                "openCommitment" => string.Equals(scope, "bond", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        private static double GetMinimumConfidence(string memoryType)
        {
            return memoryType switch
            {
                "explicitOverride" => ExplicitOverrideConfidenceThreshold,
                "explicitPreference" => ExplicitPreferenceConfidenceThreshold,
                "stableFact" => StableFactConfidenceThreshold,
                "inferredHabitCandidate" => InferredHabitCandidateConfidenceThreshold,
                "bondUpdate" => BondUpdateConfidenceThreshold,
                "openCommitment" => OpenCommitmentConfidenceThreshold,
                _ => 1d,
            };
        }

        private static string BuildDeduplicationKey(MemoryWriteEntry write)
        {
            return $"{write.MemoryType}\u001f{write.Scope}\u001f{BuildComparisonValue(write.Text)}";
        }

        private static MemoryWriteEntry SelectBestWrite(IGrouping<string, MemoryWriteEntry> group)
        {
            return group
                .OrderByDescending(write => write.Confidence)
                .ThenByDescending(write => !string.IsNullOrWhiteSpace(write.Replaces))
                .ThenByDescending(write => write.Text.Length)
                .First();
        }

        private static int GetWritePriorityScore(MemoryWriteEntry write)
        {
            return write.MemoryType switch
            {
                "explicitOverride" => 500,
                "explicitPreference" => 400,
                "stableFact" => 300,
                "inferredHabitCandidate" => 250,
                "bondUpdate" => 200,
                "openCommitment" => 100,
                _ => 0,
            };
        }

        private static string NormalizeMemoryType(string rawValue)
        {
            return NormalizeLabel(rawValue) switch
            {
                "explicitoverride" => "explicitOverride",
                "explicitpreference" => "explicitPreference",
                "stablefact" => "stableFact",
                "inferredhabitcandidate" => "inferredHabitCandidate",
                "bondupdate" => "bondUpdate",
                "opencommitment" => "openCommitment",
                "nowrite" => "noWrite",
                _ => string.Empty,
            };
        }

        private static string NormalizeScope(string rawValue)
        {
            return NormalizeLabel(rawValue) switch
            {
                "user" => "user",
                "character" => "character",
                "bond" => "bond",
                "relationship" => "bond",
                _ => string.Empty,
            };
        }

        private static string NormalizePriority(string rawValue, string memoryType)
        {
            return NormalizeLabel(rawValue) switch
            {
                "high" => "high",
                "medium" => "medium",
                "low" => "low",
                _ => memoryType switch
                {
                    "explicitOverride" => "high",
                    "explicitPreference" => "high",
                    "stableFact" => "medium",
                    "bondUpdate" => "medium",
                    "openCommitment" => "medium",
                    _ => "low",
                },
            };
        }

        private static double ClampConfidence(double rawValue)
        {
            return rawValue < 0d
                ? 0d
                : rawValue > 1d
                    ? 1d
                    : rawValue;
        }

        private static string NormalizeWriteText(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            var normalized = rawValue
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            if (normalized.StartsWith("- ", StringComparison.Ordinal) || normalized.StartsWith("* ", StringComparison.Ordinal))
            {
                normalized = normalized[2..].Trim();
            }

            normalized = normalized.Trim('"', '\'', '“', '”', '‘', '’').Trim();
            return string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }

        private static string NormalizeLabel(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? string.Empty
                : new string(rawValue
                    .Trim()
                    .Where(static character => !char.IsWhiteSpace(character) && character != '-' && character != '_')
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        private static string BuildComparisonValue(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? string.Empty
                : new string(rawValue
                    .Where(static character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && character != '`')
                    .Select(char.ToLowerInvariant)
                    .ToArray());
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

        private static Dictionary<string, object?>? ParseToolPayload(LlmResponseEnvelope response)
        {
            var toolCall = response.ToolCalls.FirstOrDefault(call =>
                string.Equals(call.Name, MemoryToolName, StringComparison.Ordinal));
            return toolCall != null && MiniJson.Deserialize(toolCall.ArgumentsJson) is Dictionary<string, object?> payload
                ? payload
                : null;
        }

        private static IReadOnlyDictionary<string, object?> BuildMemoryToolSchema()
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["writes"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["memoryType"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                    ["enum"] = new[] { "explicitOverride", "explicitPreference", "stableFact", "inferredHabitCandidate", "bondUpdate", "openCommitment", "noWrite" },
                                },
                                ["scope"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                    ["enum"] = new[] { "user", "character", "bond" },
                                },
                                ["text"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                },
                                ["priority"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                    ["enum"] = new[] { "high", "medium", "low" },
                                },
                                ["replaces"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                },
                                ["sourceThreadId"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "string",
                                },
                                ["confidence"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["type"] = "number",
                                },
                            },
                            ["required"] = new[] { "memoryType", "scope", "text", "priority", "replaces", "sourceThreadId", "confidence" },
                            ["additionalProperties"] = false,
                        },
                    },
                },
                ["required"] = new[] { "writes" },
                ["additionalProperties"] = false,
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
