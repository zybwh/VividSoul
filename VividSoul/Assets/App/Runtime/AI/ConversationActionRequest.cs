#nullable enable

namespace VividSoul.Runtime.AI
{
    public enum ConversationActionKind
    {
        None = 0,
        PlayBuiltInPose = 1,
    }

    public sealed record ConversationActionRequest(
        ConversationActionKind Kind,
        string ActionId);
}
