#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record LlmProviderProfile(
        string Id,
        string DisplayName,
        LlmProviderType ProviderType,
        string BaseUrl,
        string Model,
        bool Enabled);
}
