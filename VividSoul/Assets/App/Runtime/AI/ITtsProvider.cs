#nullable enable

namespace VividSoul.Runtime.AI
{
    public interface ITtsProvider
    {
        LlmProviderType ProviderType { get; }

        System.Threading.Tasks.Task<TtsSynthesisResult> SynthesizeAsync(TtsRequest request, System.Threading.CancellationToken cancellationToken);
    }

    public sealed record TtsRequest(
        LlmProviderProfile ProviderProfile,
        string ApiKey,
        string Text,
        float Volume,
        string PreferredVoiceId = "");

    public sealed record TtsSynthesisResult(
        string AudioUrl,
        string AudioFormat);
}
