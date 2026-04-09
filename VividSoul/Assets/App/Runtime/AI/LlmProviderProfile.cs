#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed record LlmProviderProfile(
        string Id,
        string DisplayName,
        LlmProviderType ProviderType,
        string BaseUrl,
        string Model,
        bool Enabled,
        string OpenClawGatewayWsUrl = "",
        string OpenClawAgentId = "main",
        OpenClawSessionMode OpenClawSessionMode = OpenClawSessionMode.PerCharacter,
        string OpenClawSessionKeyTemplate = "",
        bool OpenClawAutoConnect = true,
        bool OpenClawAutoReconnect = true,
        bool OpenClawReceiveProactiveMessages = true,
        bool OpenClawMirrorTranscriptLocally = true,
        bool OpenClawEnableBubbleForIncoming = true,
        bool OpenClawEnableTtsForIncoming = false,
        string MiniMaxTtsModel = "",
        string MiniMaxTtsVoiceId = "");
}
