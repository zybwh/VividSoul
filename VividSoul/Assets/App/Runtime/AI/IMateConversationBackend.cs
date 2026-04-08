#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace VividSoul.Runtime.AI
{
    public interface IMateConversationBackend : IDisposable
    {
        event Action<ConversationMessageEnvelope>? MessageReceived;

        event Action<ConversationStatusSnapshot>? StatusChanged;

        LlmProviderType ProviderType { get; }

        Task ActivateAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            CancellationToken cancellationToken);

        Task SendUserMessageAsync(
            LlmProviderProfile profile,
            string characterSourcePath,
            string characterDisplayName,
            string userMessage,
            CancellationToken cancellationToken);

        Task DeactivateAsync(CancellationToken cancellationToken);

        void MarkMessagesRead();

        void Tick(float unscaledTime);

        void NotifyApplicationFocus(bool hasFocus);
    }
}
