#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.AI
{
    public interface ILlmProvider
    {
        string ProviderId { get; }

        bool SupportsStreaming { get; }

        bool SupportsSystemPrompt { get; }

    bool SupportsToolCalls { get; }

        Task<LlmResponseEnvelope> GenerateAsync(LlmRequestContext request, CancellationToken cancellationToken);
    }

    public sealed record LlmRequestContext(
        LlmProviderProfile ProviderProfile,
        string ApiKey,
        string SystemPrompt,
        float Temperature,
        int MaxOutputTokens,
        bool EnableStreaming,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<LlmToolDefinition>? Tools = null,
    string ForcedToolName = "");

public sealed record LlmToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, object?> ParametersSchema);

public sealed record LlmToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

    public sealed record LlmResponseEnvelope(
        string DisplayText,
        string TtsText,
        bool ShouldSpeak,
        string ProviderId,
        string Model,
        int PromptCharacters,
    int CompletionCharacters,
    string RawText,
    IReadOnlyList<LlmToolCall> ToolCalls);
}
