#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record ConversationStatusSnapshot(
        string ProviderId,
        string ProviderDisplayName,
        LlmProviderType ProviderType,
        ConversationConnectionState ConnectionState,
        string StatusText,
        string SessionKey,
        string AgentId,
        bool IsRequestInFlight,
        int UnreadCount);
}
