#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VividSoul.Runtime.Animation;

namespace VividSoul.Runtime.AI
{
    public sealed class ActionIntentJudge
    {
        private const string ActionToolName = "submit_action_intent";
        private const double PlayBuiltInPoseConfidenceThreshold = 0.82d;
        private const int JudgeMaxOutputTokens = 320;
        private static readonly LlmToolDefinition ActionIntentTool = new(
            Name: ActionToolName,
            Description: "Submit the structured action intent for the latest exchange.",
            ParametersSchema: BuildActionToolSchema());
        private readonly ILlmProvider openAiCompatibleProvider;
        private readonly ILlmProvider miniMaxProvider;

        public ActionIntentJudge(
            ILlmProvider openAiCompatibleProvider,
            ILlmProvider miniMaxProvider)
        {
            this.openAiCompatibleProvider = openAiCompatibleProvider ?? throw new ArgumentNullException(nameof(openAiCompatibleProvider));
            this.miniMaxProvider = miniMaxProvider ?? throw new ArgumentNullException(nameof(miniMaxProvider));
        }

        public async Task<ActionIntentDecision> JudgeAsync(
            LlmProviderProfile providerProfile,
            string apiKey,
            string characterDisplayName,
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

            if (string.IsNullOrWhiteSpace(assistantMessage))
            {
                return ActionIntentDecision.NoAction(sourceThreadId);
            }

            var requestContext = new LlmRequestContext(
                ProviderProfile: providerProfile,
                ApiKey: apiKey.Trim(),
                SystemPrompt: BuildSystemPrompt(characterDisplayName),
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
                Tools: new[] { ActionIntentTool },
                ForcedToolName: ActionToolName);
            var provider = ResolveProvider(providerProfile.ProviderType);
            var response = await provider.GenerateAsync(requestContext, cancellationToken);
            return ParseDecision(response, sourceThreadId);
        }

        private static string BuildSystemPrompt(string characterDisplayName)
        {
            var normalizedDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? "VividSoul Mate"
                : characterDisplayName.Trim();
            var actionCatalog = string.Join(
                "\n",
                BuiltInPoseCatalog.All.Select(pose =>
                    $"- {pose.Id}: {pose.Label}. {pose.Description}"));
            return string.Join(
                "\n\n",
                new[]
                {
                    "Judge whether the latest assistant reply should be paired with exactly one built-in body action.",
                    "Most replies should choose noAction.",
                    "Use playBuiltInPose only when the body language is obvious, short, and clearly improves embodiment.",
                    "Prefer noAction for reminders, factual answers, explanations, long guidance, refusals, corrections, or generic chat.",
                    "Avoid exaggerated poses unless the assistant reply is clearly playful and strongly matches the pose.",
                    "Think briefly and call the tool as soon as the decision is clear.",
                    "Call the tool exactly once.",
                    "Keep note short plain text without markdown.",
                    $"Current character: {normalizedDisplayName}.",
                    $"Available built-in poses:\n{actionCatalog}",
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
                    "Analyze whether the assistant reply should trigger a built-in pose right now.",
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

        private static ActionIntentDecision ParseDecision(LlmResponseEnvelope response, string sourceThreadId)
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
                return ActionIntentDecision.NoAction(sourceThreadId);
            }

            var operation = NormalizeOperation(ReadString(payload, "operation"));
            if (string.IsNullOrWhiteSpace(operation))
            {
                return ActionIntentDecision.NoAction(sourceThreadId);
            }

            var decision = new ActionIntentDecision(
                Operation: operation,
                ActionId: NormalizeText(ReadString(payload, "actionId")),
                Note: NormalizeText(ReadString(payload, "note")),
                Confidence: ClampConfidence(ReadDouble(payload, "confidence")),
                SourceThreadId: sourceThreadId);
            return NormalizeDecision(decision);
        }

        private static ActionIntentDecision NormalizeDecision(ActionIntentDecision decision)
        {
            if (!string.Equals(decision.Operation, "playBuiltInPose", StringComparison.Ordinal))
            {
                return ActionIntentDecision.NoAction(decision.SourceThreadId);
            }

            if (decision.Confidence < PlayBuiltInPoseConfidenceThreshold
                || string.IsNullOrWhiteSpace(decision.ActionId))
            {
                return ActionIntentDecision.NoAction(decision.SourceThreadId);
            }

            return BuiltInPoseCatalog.All.Any(pose =>
                       string.Equals(pose.Id, decision.ActionId, StringComparison.OrdinalIgnoreCase))
                ? decision
                : ActionIntentDecision.NoAction(decision.SourceThreadId);
        }

        private static string NormalizeOperation(string rawValue)
        {
            return NormalizeLabel(rawValue) switch
            {
                "playbuiltinpose" => "playBuiltInPose",
                "noaction" => "noAction",
                "none" => "noAction",
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
                _ when double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
                _ => 0d,
            };
        }

        private static Dictionary<string, object?>? ParseToolPayload(LlmResponseEnvelope response)
        {
            var toolCall = response.ToolCalls.FirstOrDefault(call =>
                string.Equals(call.Name, ActionToolName, StringComparison.Ordinal));
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

        private static IReadOnlyDictionary<string, object?> BuildActionToolSchema()
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["operation"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["enum"] = new[] { "playBuiltInPose", "noAction" },
                    },
                    ["actionId"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                        ["description"] = "Built-in pose id when operation=playBuiltInPose. Otherwise use an empty string.",
                    },
                    ["note"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "string",
                    },
                    ["confidence"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "number",
                    },
                },
                ["required"] = new[] { "operation", "actionId", "note", "confidence" },
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
                ["actionId"] = MatchStringField(rawArguments, "actionId"),
                ["note"] = MatchStringField(rawArguments, "note"),
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

    public sealed record ActionIntentDecision(
        string Operation,
        string ActionId,
        string Note,
        double Confidence,
        string SourceThreadId)
    {
        public static ActionIntentDecision NoAction(string sourceThreadId)
        {
            return new ActionIntentDecision("noAction", string.Empty, string.Empty, 0d, sourceThreadId);
        }
    }
}
