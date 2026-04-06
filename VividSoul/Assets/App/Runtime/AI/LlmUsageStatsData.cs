#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record LlmUsageStatsData(
        int TotalRequestCount,
        int SuccessfulRequestCount,
        int FailedRequestCount,
        long TotalLatencyMs,
        long TotalPromptCharacters,
        long TotalCompletionCharacters,
        string LastProviderId,
        string LastModel,
        string LastRequestAtUtc,
        string LastErrorMessage);
}
