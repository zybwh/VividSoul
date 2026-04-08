#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime;

namespace VividSoul.Runtime.AI
{
    public sealed class ReminderIntentJudge
    {
        private const string ReminderToolName = "submit_reminder_intent";
        private const double CreateConfidenceThreshold = 0.75d;
        private const double CancelConfidenceThreshold = 0.72d;
        private const double CompleteConfidenceThreshold = 0.72d;
        private const int JudgeMaxOutputTokens = 512;
        private static readonly LlmToolDefinition ReminderIntentTool = new(
            Name: ReminderToolName,
            Description: "Submit the reminder intent for the latest user message.",
            ParametersSchema: BuildReminderToolSchema());
        private readonly ILlmProvider openAiCompatibleProvider;
        private readonly ILlmProvider miniMaxProvider;
        private readonly ReminderStore reminderStore;

        public ReminderIntentJudge(
            ILlmProvider openAiCompatibleProvider,
            ILlmProvider miniMaxProvider,
            ReminderStore? reminderStore = null)
        {
            this.openAiCompatibleProvider = openAiCompatibleProvider ?? throw new ArgumentNullException(nameof(openAiCompatibleProvider));
            this.miniMaxProvider = miniMaxProvider ?? throw new ArgumentNullException(nameof(miniMaxProvider));
            this.reminderStore = reminderStore ?? new ReminderStore();
        }

        public async Task<ReminderIntentDecision> JudgeAsync(
            LlmProviderProfile providerProfile,
            string apiKey,
            string characterDisplayName,
            string characterFingerprint,
            string userMessage,
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

            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return ReminderIntentDecision.NoIntent(sourceThreadId);
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
                        Text: BuildJudgeInput(userMessage, sourceThreadId),
                        CreatedAt: DateTimeOffset.UtcNow,
                        Source: ChatInvocationSource.System),
                },
                Tools: new[] { ReminderIntentTool },
                ForcedToolName: ReminderToolName);

            var provider = ResolveProvider(providerProfile.ProviderType);
            var response = await provider.GenerateAsync(requestContext, cancellationToken);
            return ParseDecision(response, sourceThreadId);
        }

        private string BuildSystemPrompt(string characterDisplayName, string characterFingerprint)
        {
            var localNow = DateTimeOffset.Now;
            string timezoneId;
            try
            {
                timezoneId = TimeZoneInfo.Local.Id;
            }
            catch
            {
                timezoneId = "UTC";
            }

            var normalizedDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? "VividSoul Mate"
                : characterDisplayName.Trim();
            return string.Join(
                "\n\n",
                new[]
                {
                    "Judge only the latest user message for reminder intent.",
                    "Think briefly and call the tool as soon as the decision is clear.",
                    "Call the tool exactly once.",
                    "Prefer precision. If reminder intent is weak or unclear, choose noReminderIntent.",
                    "Use create only when the reminder target is clear and the time can be resolved into an absolute UTC timestamp.",
                    "If the user wants a reminder but the time or target is still ambiguous, choose needsClarification and explain the ambiguity briefly in Chinese.",
                    "For cancel or complete, fill title when the user names the reminder clearly. Otherwise leave title empty and copy the relevant user wording into sourceText.",
                    "If the user says things like 刚才那个提醒, 那个提醒, 上个提醒, or 刚刚那个提醒, resolve the target against the pending reminder list before deciding.",
                    "Keep title, note, and sourceText short plain text without markdown.",
                    "Do not restate the full timeline or write a long analysis before the tool call.",
                    "Local context:",
                    $"localNow: {localNow:O}",
                    $"timezone: {timezoneId}",
                    $"currentCharacter: {normalizedDisplayName}",
                    $"pendingReminders:\n{reminderStore.BuildPendingReminderPromptContext(characterFingerprint, 4)}",
                    "Do not answer with prose. Use the tool.",
                });
        }

        private static string BuildJudgeInput(string userMessage, string sourceThreadId)
        {
            return string.Join(
                "\n",
                new[]
                {
                    $"sourceThreadId: {sourceThreadId}",
                    $"latestUserMessage: {userMessage.Trim()}",
                    "Analyze only the latest user message.",
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

        private static ReminderIntentDecision ParseDecision(LlmResponseEnvelope response, string sourceThreadId)
        {
            var payload = ParseToolPayload(response);
            if (payload == null
                && !string.IsNullOrWhiteSpace(response.RawText)
                && MiniJson.Deserialize(response.RawText) is Dictionary<string, object?> fallbackPayload)
            {
                payload = fallbackPayload;
            }

            if (payload == null)
            {
                return ReminderIntentDecision.NoIntent(sourceThreadId);
            }

            var operation = NormalizeOperation(ReadString(payload, "operation"));
            if (string.IsNullOrWhiteSpace(operation))
            {
                return ReminderIntentDecision.NoIntent(sourceThreadId);
            }

            var decision = new ReminderIntentDecision(
                Operation: operation,
                Title: NormalizeText(ReadString(payload, "title")),
                Note: NormalizeText(ReadString(payload, "note")),
                DueAtUtc: ParseUtcTimestamp(ReadString(payload, "dueAtUtc")),
                SourceText: NormalizeText(ReadString(payload, "sourceText")),
                Confidence: ClampConfidence(ReadDouble(payload, "confidence")),
                SourceThreadId: sourceThreadId);
            return NormalizeDecision(decision);
        }

        private static ReminderIntentDecision NormalizeDecision(ReminderIntentDecision decision)
        {
            if (string.Equals(decision.Operation, "create", StringComparison.Ordinal))
            {
                if (decision.DueAtUtc == null
                    || decision.Confidence < CreateConfidenceThreshold
                    || string.IsNullOrWhiteSpace(decision.Title))
                {
                    return ReminderIntentDecision.NeedsClarification(
                        decision.SourceThreadId,
                        string.IsNullOrWhiteSpace(decision.Note) ? "提醒时间还不够明确。" : decision.Note);
                }

                return decision;
            }

            if (string.Equals(decision.Operation, "cancel", StringComparison.Ordinal))
            {
                return decision.Confidence >= CancelConfidenceThreshold
                    ? decision
                    : ReminderIntentDecision.NoIntent(decision.SourceThreadId);
            }

            if (string.Equals(decision.Operation, "complete", StringComparison.Ordinal))
            {
                return decision.Confidence >= CompleteConfidenceThreshold
                    ? decision
                    : ReminderIntentDecision.NoIntent(decision.SourceThreadId);
            }

            if (string.Equals(decision.Operation, "needsClarification", StringComparison.Ordinal))
            {
                return decision with
                {
                    Note = string.IsNullOrWhiteSpace(decision.Note) ? "提醒时间还不够明确。" : decision.Note,
                };
            }

            return decision.Operation switch
            {
                "noReminderIntent" => ReminderIntentDecision.NoIntent(decision.SourceThreadId),
                _ => ReminderIntentDecision.NoIntent(decision.SourceThreadId),
            };
        }

        private static string NormalizeOperation(string rawValue)
        {
            return NormalizeLabel(rawValue) switch
            {
                "create" => "create",
                "cancel" => "cancel",
                "complete" => "complete",
                "needsclarification" => "needsClarification",
                "noreminderintent" => "noReminderIntent",
                "none" => "noReminderIntent",
                _ => string.Empty,
            };
        }

        private static string NormalizeText(string rawValue)
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
            return string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized.Trim('"', '\'', '“', '”', '‘', '’').Trim();
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

        private static DateTimeOffset? ParseUtcTimestamp(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)
                || !DateTimeOffset.TryParse(rawValue.Trim(), out var timestamp))
            {
                return null;
            }

            return timestamp.ToUniversalTime();
        }

        private static double ClampConfidence(double rawValue)
        {
            return rawValue < 0d
                ? 0d
                : rawValue > 1d
                    ? 1d
                    : rawValue;
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
                string.Equals(call.Name, ReminderToolName, StringComparison.Ordinal));
            if (toolCall == null)
            {
                return null;
            }

            try
            {
                return MiniJson.Deserialize(toolCall.ArgumentsJson) is Dictionary<string, object?> payload
                    ? payload
                    : ParseLooseToolPayload(toolCall.ArgumentsJson);
            }
            catch (FormatException)
            {
                return ParseLooseToolPayload(toolCall.ArgumentsJson);
            }
        }

        private static IReadOnlyDictionary<string, object?> BuildReminderToolSchema()
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["operation"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["enum"] = new[] { "create", "cancel", "complete", "needsClarification", "noReminderIntent" },
                    },
                    ["title"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                    },
                    ["note"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                    },
                    ["dueAtUtc"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["description"] = "Absolute UTC timestamp in ISO 8601 format when operation=create. Otherwise use an empty string.",
                    },
                    ["sourceText"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                    },
                    ["confidence"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "number",
                    },
                },
                ["required"] = new[] { "operation", "title", "note", "dueAtUtc", "sourceText", "confidence" },
                ["additionalProperties"] = false,
            };
        }

        private static Dictionary<string, object?>? ParseLooseToolPayload(string rawArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArguments))
            {
                return null;
            }

            var operation = MatchStringField(rawArguments, "operation");
            if (string.IsNullOrWhiteSpace(operation))
            {
                return null;
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["operation"] = operation,
                ["title"] = MatchStringField(rawArguments, "title"),
                ["note"] = MatchStringField(rawArguments, "note"),
                ["dueAtUtc"] = MatchStringField(rawArguments, "dueAtUtc"),
                ["sourceText"] = MatchStringField(rawArguments, "sourceText"),
                ["confidence"] = MatchNumberField(rawArguments, "confidence"),
            };
        }

        private static string MatchStringField(string rawArguments, string fieldName)
        {
            var match = Regex.Match(
                rawArguments,
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return string.Empty;
            }

            return match.Groups["value"].Value
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal);
        }

        private static double MatchNumberField(string rawArguments, string fieldName)
        {
            var match = Regex.Match(
                rawArguments,
                $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>-?\\d+(?:\\.\\d+)?)",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            return match.Success && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0d;
        }
    }

    public sealed record ReminderIntentDecision(
        string Operation,
        string Title,
        string Note,
        DateTimeOffset? DueAtUtc,
        string SourceText,
        double Confidence,
        string SourceThreadId)
    {
        public static ReminderIntentDecision NoIntent(string sourceThreadId)
        {
            return new ReminderIntentDecision("noReminderIntent", string.Empty, string.Empty, null, string.Empty, 0d, sourceThreadId);
        }

        public static ReminderIntentDecision NeedsClarification(string sourceThreadId, string note)
        {
            return new ReminderIntentDecision("needsClarification", string.Empty, note, null, string.Empty, 1d, sourceThreadId);
        }
    }
}
