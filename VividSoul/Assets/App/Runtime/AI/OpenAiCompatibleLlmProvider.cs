#nullable enable

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VividSoul.Runtime;

namespace VividSoul.Runtime.AI
{
    public class OpenAiCompatibleLlmProvider : ILlmProvider
    {
        private readonly LlmProviderType providerType;
        private readonly bool enableReasoningSplit;

        public bool SupportsStreaming => false;

        public bool SupportsSystemPrompt => true;

        public OpenAiCompatibleLlmProvider()
            : this(LlmProviderType.OpenAiCompatible, false)
        {
        }

        protected OpenAiCompatibleLlmProvider(LlmProviderType providerType, bool enableReasoningSplit)
        {
            this.providerType = providerType;
            this.enableReasoningSplit = enableReasoningSplit;
        }

        public string ProviderId => providerType.ToString();

        public async Task<LlmResponseEnvelope> GenerateAsync(LlmRequestContext request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.ProviderProfile == null)
            {
                throw new ArgumentException("A provider profile is required.", nameof(request));
            }

            var baseUrl = request.ProviderProfile.BaseUrl?.Trim() ?? string.Empty;
            var model = request.ProviderProfile.Model?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new UserFacingException("当前 Provider 缺少 API URL。");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new UserFacingException("当前 Provider 缺少模型名称。");
            }

            var endpoint = ResolveChatCompletionsEndpoint(baseUrl);
            var requestJson = BuildRequestJson(request);
            var promptCharacters = request.SystemPrompt.Length + request.Messages.Sum(static message => message.Text.Length);

            using var unityWebRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            unityWebRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            unityWebRequest.SetRequestHeader("Content-Type", "application/json");
            var normalizedAuthorizationValue = BuildAuthorizationHeaderValue(request.ApiKey);
            if (!string.IsNullOrWhiteSpace(normalizedAuthorizationValue))
            {
                unityWebRequest.SetRequestHeader("Authorization", normalizedAuthorizationValue);
            }

            using var cancellationRegistration = cancellationToken.Register(unityWebRequest.Abort);
            var operation = unityWebRequest.SendWebRequest();
            await AwaitOperationAsync(operation, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var responseText = unityWebRequest.downloadHandler.text ?? string.Empty;
            if (unityWebRequest.result != UnityWebRequest.Result.Success)
            {
                throw new UserFacingException(BuildFailureMessage(unityWebRequest, responseText));
            }

            var responseFile = JsonUtility.FromJson<ChatCompletionsResponseFile>(responseText);
            var rawContent = ExtractRawContent(responseFile);
            var displayText = LlmDialogueTextFormatter.ToDisplayRichText(rawContent);
            var ttsText = LlmDialogueTextFormatter.ToPlainDialogueText(rawContent);
            if (string.IsNullOrWhiteSpace(displayText))
            {
                throw new UserFacingException("LLM 返回了空内容。");
            }

            return new LlmResponseEnvelope(
                DisplayText: displayText,
                TtsText: string.IsNullOrWhiteSpace(ttsText) ? displayText : ttsText,
                ShouldSpeak: true,
                ProviderId: request.ProviderProfile.Id,
                Model: string.IsNullOrWhiteSpace(responseFile?.model) ? model : responseFile!.model,
                PromptCharacters: promptCharacters,
                CompletionCharacters: (string.IsNullOrWhiteSpace(ttsText) ? displayText : ttsText).Length);
        }

        private static async Task AwaitOperationAsync(UnityWebRequestAsyncOperation operation, CancellationToken cancellationToken)
        {
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static string ResolveChatCompletionsEndpoint(string baseUrl)
        {
            var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
            return normalizedBaseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? normalizedBaseUrl
                : $"{normalizedBaseUrl}/chat/completions";
        }

        private static string BuildAuthorizationHeaderValue(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }

            var normalized = apiKey.Trim().Trim('"', '\'');
            if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[7..].Trim();
            }

            normalized = normalized
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (normalized.Any(char.IsControl))
            {
                throw new UserFacingException("API Key 包含非法控制字符，请重新粘贴后再试。");
            }

            return $"Bearer {normalized}";
        }

        private static string BuildFailureMessage(UnityWebRequest unityWebRequest, string responseText)
        {
            var responseFile = string.IsNullOrWhiteSpace(responseText)
                ? null
                : JsonUtility.FromJson<ChatCompletionsResponseFile>(responseText);
            var responseError = responseFile?.error?.message;
            if (!string.IsNullOrWhiteSpace(responseError))
            {
                return $"LLM 请求失败：{responseError.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(unityWebRequest.error))
            {
                return $"LLM 请求失败：{unityWebRequest.error.Trim()}";
            }

            return $"LLM 请求失败：HTTP {(long)unityWebRequest.responseCode}";
        }

        private static string ExtractRawContent(ChatCompletionsResponseFile? responseFile)
        {
            var choice = responseFile?.choices != null && responseFile.choices.Length > 0
                ? responseFile.choices[0]
                : null;
            return choice?.message?.content ?? string.Empty;
        }

        private string BuildRequestJson(LlmRequestContext context)
        {
            return enableReasoningSplit
                ? JsonUtility.ToJson(MiniMaxChatCompletionsRequestFile.FromContext(context))
                : JsonUtility.ToJson(ChatCompletionsRequestFile.FromContext(context));
        }

        [Serializable]
        private sealed class ChatCompletionsRequestFile
        {
            public string model = string.Empty;
            public float temperature = 0.8f;
            public int max_tokens = 256;
            public bool stream;
            public ChatCompletionsMessageFile[] messages = Array.Empty<ChatCompletionsMessageFile>();

            public static ChatCompletionsRequestFile FromContext(LlmRequestContext context)
            {
                var requestMessages = context.Messages
                    .Select(static message => ChatCompletionsMessageFile.FromMessage(message))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(context.SystemPrompt))
                {
                    requestMessages.Insert(0, new ChatCompletionsMessageFile
                    {
                        role = "system",
                        content = context.SystemPrompt.Trim(),
                    });
                }

                return new ChatCompletionsRequestFile
                {
                    model = context.ProviderProfile.Model.Trim(),
                    temperature = context.Temperature,
                    max_tokens = context.MaxOutputTokens,
                    stream = false,
                    messages = requestMessages.ToArray(),
                };
            }
        }

        [Serializable]
        private sealed class MiniMaxChatCompletionsRequestFile
        {
            public string model = string.Empty;
            public float temperature = 0.8f;
            public int max_tokens = 256;
            public bool stream;
            public bool reasoning_split = true;
            public ChatCompletionsMessageFile[] messages = Array.Empty<ChatCompletionsMessageFile>();

            public static MiniMaxChatCompletionsRequestFile FromContext(LlmRequestContext context)
            {
                var baseRequest = ChatCompletionsRequestFile.FromContext(context);
                return new MiniMaxChatCompletionsRequestFile
                {
                    model = baseRequest.model,
                    temperature = baseRequest.temperature,
                    max_tokens = baseRequest.max_tokens,
                    stream = baseRequest.stream,
                    reasoning_split = true,
                    messages = baseRequest.messages,
                };
            }
        }

        [Serializable]
        private sealed class ChatCompletionsMessageFile
        {
            public string role = "user";
            public string content = string.Empty;

            public static ChatCompletionsMessageFile FromMessage(ChatMessage message)
            {
                return new ChatCompletionsMessageFile
                {
                    role = message.Role switch
                    {
                        ChatRole.System => "system",
                        ChatRole.Assistant => "assistant",
                        _ => "user",
                    },
                    content = message.Text,
                };
            }
        }

        [Serializable]
        private sealed class ChatCompletionsResponseFile
        {
            public string model = string.Empty;
            public ChatCompletionsChoiceFile[] choices = Array.Empty<ChatCompletionsChoiceFile>();
            public ChatCompletionsErrorFile? error;
        }

        [Serializable]
        private sealed class ChatCompletionsChoiceFile
        {
            public ChatCompletionsMessageFile? message;
        }

        [Serializable]
        private sealed class ChatCompletionsErrorFile
        {
            public string message = string.Empty;
        }
    }
}
