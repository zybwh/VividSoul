#nullable enable

using System;

namespace VividSoul.Runtime.AI
{
    public enum ChatRole
    {
        System = 0,
        User = 1,
        Assistant = 2,
    }

    public enum ChatInvocationSource
    {
        UserInput = 0,
        ProactiveTick = 1,
        System = 2,
    }

    public sealed record ChatMessage(
        string Id,
        string SessionId,
        ChatRole Role,
        string Text,
        DateTimeOffset CreatedAt,
        ChatInvocationSource Source);
}
