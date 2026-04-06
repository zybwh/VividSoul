#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record AiSettingsData(
        string ActiveProviderId,
        string GlobalSystemPrompt,
        float Temperature,
        int MaxOutputTokens,
        bool EnableStreaming,
        bool EnableProactiveMessages,
        float ProactiveMinIntervalMinutes,
        float ProactiveMaxIntervalMinutes,
        int MemoryWindowTurns,
        int SummaryThreshold,
        bool EnableTts,
        System.Collections.Generic.IReadOnlyList<LlmProviderProfile> ProviderProfiles);
}
