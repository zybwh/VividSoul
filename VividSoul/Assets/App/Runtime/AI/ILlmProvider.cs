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

        Task<LlmResponseEnvelope> GenerateAsync(LlmRequestContext request, CancellationToken cancellationToken);
    }

    public sealed record LlmRequestContext(
        LlmProviderProfile ProviderProfile,
        string ApiKey,
        string SystemPrompt,
        float Temperature,
        int MaxOutputTokens,
        bool EnableStreaming,
        IReadOnlyList<ChatMessage> Messages);

    public sealed record LlmResponseEnvelope(
        string DisplayText,
        string TtsText,
        bool ShouldSpeak,
        string ProviderId,
        string Model,
        int PromptCharacters,
        int CompletionCharacters);
}
