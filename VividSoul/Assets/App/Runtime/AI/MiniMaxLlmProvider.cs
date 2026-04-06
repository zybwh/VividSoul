#nullable enable

namespace VividSoul.Runtime.AI
{
    public sealed class MiniMaxLlmProvider : OpenAiCompatibleLlmProvider
    {
        public MiniMaxLlmProvider()
            : base(LlmProviderType.MiniMax, true)
        {
        }
    }
}
