#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record ConversationMessageEnvelope(
        ChatMessage Message,
        bool IsProactive,
        bool ShouldDisplayBubble,
        bool ShouldSpeak,
        bool IsOptimistic);
}
