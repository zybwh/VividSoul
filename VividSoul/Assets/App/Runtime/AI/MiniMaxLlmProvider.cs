#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed class MiniMaxLlmProvider : OpenAiCompatibleLlmProvider
    {
        public MiniMaxLlmProvider(bool enableReasoningSplit = true)
            : base(LlmProviderType.MiniMax, enableReasoningSplit)
        {
        }
    }
}
