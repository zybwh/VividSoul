#nullable enable

namespace VividSoul.Runtime.AI
{
    public enum ConversationConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        AuthFailed = 4,
        Faulted = 5,
    }
}
