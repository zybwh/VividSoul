#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
        private const int RequestTimeoutSeconds = 45;
        private const int MiniMaxRetryMinTokens = 768;
        private const int MiniMaxToolRetryMinTokens = 1024;
        private const int MiniMaxRetryMaxTokens = 1536;
        private readonly LlmProviderType providerType;
        private readonly bool enableReasoningSplit;

        public bool SupportsStreaming => false;

        public bool SupportsSystemPrompt => true;

        public bool SupportsToolCalls => true;

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
            var promptCharacters = request.SystemPrompt.Length
                + request.Messages.Sum(static message => message.Text.Length)
                + CountToolSchemaCharacters(request.Tools);
            var responseText = ShouldUseHttpClientTransport()
                ? await SendWithHttpClientAsync(request, endpoint, cancellationToken)
                : await SendWithUnityWebRequestAsync(request, endpoint, cancellationToken);
            return await FinalizeResponseAsync(
                request,
                endpoint,
                model,
                promptCharacters,
                responseText,
                cancellationToken,
                allowRetry: true);
        }

        private async Task<LlmResponseEnvelope> FinalizeResponseAsync(
            LlmRequestContext request,
            string endpoint,
            string fallbackModel,
            int promptCharacters,
            string responseText,
            CancellationToken cancellationToken,
            bool allowRetry)
        {
            if (TryBuildResponseEnvelope(
                    request,
                    fallbackModel,
                    promptCharacters,
                    responseText,
                    out var response,
                    out var finishReason,
                    out var failureMessage))
            {
                return response;
            }

            if (allowRetry && ShouldRetryMiniMaxEmptyResponse(request, finishReason))
            {
                var retryRequest = request with
                {
                    MaxOutputTokens = ComputeRetryMaxTokens(request),
                };
                var retryResponseText = ShouldUseHttpClientTransport()
                    ? await SendWithHttpClientAsync(retryRequest, endpoint, cancellationToken)
                    : await SendWithUnityWebRequestAsync(retryRequest, endpoint, cancellationToken);
                return await FinalizeResponseAsync(
                    retryRequest,
                    endpoint,
                    fallbackModel,
                    promptCharacters,
                    retryResponseText,
                    cancellationToken,
                    allowRetry: false);
            }

            throw new UserFacingException(failureMessage);
        }

        private bool ShouldRetryMiniMaxEmptyResponse(LlmRequestContext request, string finishReason)
        {
            if (providerType != LlmProviderType.MiniMax
                || !enableReasoningSplit
                || !string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return request.MaxOutputTokens < MiniMaxRetryMaxTokens;
        }

        private static int ComputeRetryMaxTokens(LlmRequestContext request)
        {
            var retryMinimum = request.Tools is { Count: > 0 }
                ? MiniMaxToolRetryMinTokens
                : MiniMaxRetryMinTokens;
            var expandedBudget = Math.Max(request.MaxOutputTokens * 2, request.MaxOutputTokens + 256);
            return Math.Min(Math.Max(retryMinimum, expandedBudget), MiniMaxRetryMaxTokens);
        }

        private async Task<string> SendWithUnityWebRequestAsync(
            LlmRequestContext request,
            string endpoint,
            CancellationToken cancellationToken)
        {
            var requestJson = BuildRequestJson(request);
            if (ShouldUseHttpClientTransport())
            {
                return await SendWithHttpClientAsync(request, endpoint, cancellationToken);
            }

            using var unityWebRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            unityWebRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            unityWebRequest.timeout = RequestTimeoutSeconds;
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

            return responseText;
        }

        private static bool ShouldUseHttpClientTransport()
        {
            try
            {
                return Application.isBatchMode;
            }
            catch (UnityException)
            {
                return true;
            }
        }

        private async Task<string> SendWithHttpClientAsync(
            LlmRequestContext request,
            string endpoint,
            CancellationToken cancellationToken)
        {
            var requestJson = BuildRequestJson(request);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
            };
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
            };
            var normalizedAuthorizationValue = BuildAuthorizationHeaderValue(request.ApiKey);
            if (!string.IsNullOrWhiteSpace(normalizedAuthorizationValue))
            {
                httpRequest.Headers.TryAddWithoutValidation("Authorization", normalizedAuthorizationValue);
            }

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new UserFacingException(BuildFailureMessage(response.ReasonPhrase ?? string.Empty, (long)response.StatusCode, responseText));
            }

            return responseText;
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
            var responseError = TryReadErrorMessage(responseText);
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

        private static string BuildFailureMessage(string reasonPhrase, long statusCode, string responseText)
        {
            var responseError = TryReadErrorMessage(responseText);
            if (!string.IsNullOrWhiteSpace(responseError))
            {
                return $"LLM 请求失败：{responseError.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(reasonPhrase))
            {
                return $"LLM 请求失败：{reasonPhrase.Trim()}";
            }

            return $"LLM 请求失败：HTTP {statusCode}";
        }

        private string BuildRequestJson(LlmRequestContext context)
        {
            var requestPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["model"] = context.ProviderProfile.Model.Trim(),
                ["temperature"] = context.Temperature,
                ["max_tokens"] = context.MaxOutputTokens,
                ["stream"] = false,
                ["messages"] = BuildRequestMessages(context),
            };
            if (enableReasoningSplit)
            {
                requestPayload["reasoning_split"] = true;
            }

            if (context.Tools is { Count: > 0 })
            {
                requestPayload["tools"] = context.Tools
                    .Select(BuildRequestTool)
                    .Cast<object?>()
                    .ToArray();
                if (!string.IsNullOrWhiteSpace(context.ForcedToolName))
                {
                    requestPayload["tool_choice"] = BuildToolChoice(context.ForcedToolName);
                }
            }

            return MiniJson.Serialize(requestPayload);
        }

        private static object[] BuildRequestMessages(LlmRequestContext context)
        {
            var requestMessages = context.Messages
                .Select(BuildRequestMessage)
                .Cast<object>()
                .ToList();
            if (!string.IsNullOrWhiteSpace(context.SystemPrompt))
            {
                requestMessages.Insert(0, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["role"] = "system",
                    ["content"] = context.SystemPrompt.Trim(),
                });
            }

            return requestMessages.ToArray();
        }

        private static Dictionary<string, object?> BuildRequestMessage(ChatMessage message)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = message.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    _ => "user",
                },
                ["content"] = message.Text,
            };
        }

        private static Dictionary<string, object?> BuildRequestTool(LlmToolDefinition tool)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.ParametersSchema,
                },
            };
        }

        private static Dictionary<string, object?> BuildToolChoice(string toolName)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = toolName.Trim(),
                },
            };
        }

        private static int CountToolSchemaCharacters(IReadOnlyList<LlmToolDefinition>? tools)
        {
            if (tools == null || tools.Count == 0)
            {
                return 0;
            }

            return tools.Sum(tool =>
                (tool.Name?.Length ?? 0)
                + (tool.Description?.Length ?? 0)
                + MiniJson.Serialize(tool.ParametersSchema).Length);
        }

        private static string TryReadErrorMessage(string responseText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(responseText)
                    || MiniJson.Deserialize(responseText) is not Dictionary<string, object?> root
                    || !root.TryGetValue("error", out var errorValue)
                    || errorValue is not Dictionary<string, object?> errorPayload)
                {
                    return string.Empty;
                }

                return ReadString(errorPayload, "message");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryBuildResponseEnvelope(
            LlmRequestContext request,
            string fallbackModel,
            int promptCharacters,
            string responseText,
            out LlmResponseEnvelope response,
            out string finishReason,
            out string failureMessage)
        {
            response = null!;
            finishReason = string.Empty;
            failureMessage = "LLM 返回了空内容。";
            if (MiniJson.Deserialize(responseText) is not Dictionary<string, object?> root)
            {
                failureMessage = "LLM 返回了无法解析的响应。";
                return false;
            }

            var message = ExtractFirstMessage(root);
            finishReason = ExtractFirstChoiceFinishReason(root);
            var rawContent = ExtractRawContent(message);
            var toolCalls = ExtractToolCalls(message);
            var displayText = LlmDialogueTextFormatter.ToDisplayRichText(rawContent);
            var ttsText = LlmDialogueTextFormatter.ToPlainDialogueText(rawContent);
            if (string.IsNullOrWhiteSpace(displayText) && toolCalls.Count == 0)
            {
                var diagnosticPreview = BuildResponsePreview(responseText);
                failureMessage = request.Tools is { Count: > 0 } && !string.IsNullOrWhiteSpace(diagnosticPreview)
                    ? $"LLM 返回了空内容。响应预览：{diagnosticPreview}"
                    : "LLM 返回了空内容。";
                return false;
            }

            var normalizedTtsText = string.IsNullOrWhiteSpace(ttsText) ? displayText : ttsText;
            response = new LlmResponseEnvelope(
                DisplayText: displayText,
                TtsText: normalizedTtsText,
                ShouldSpeak: !string.IsNullOrWhiteSpace(displayText),
                ProviderId: request.ProviderProfile.Id,
                Model: string.IsNullOrWhiteSpace(ReadString(root, "model")) ? fallbackModel : ReadString(root, "model"),
                PromptCharacters: promptCharacters,
                CompletionCharacters: CalculateCompletionCharacters(normalizedTtsText, rawContent, toolCalls),
                RawText: rawContent,
                ToolCalls: toolCalls);
            return true;
        }

        private static Dictionary<string, object?>? ExtractFirstMessage(IReadOnlyDictionary<string, object?> root)
        {
            if (!root.TryGetValue("choices", out var choicesValue)
                || choicesValue is not List<object?> choices
                || choices.Count == 0
                || choices[0] is not Dictionary<string, object?> choice
                || !choice.TryGetValue("message", out var messageValue)
                || messageValue is not Dictionary<string, object?> message)
            {
                return null;
            }

            return message;
        }

        private static string ExtractFirstChoiceFinishReason(IReadOnlyDictionary<string, object?> root)
        {
            if (!root.TryGetValue("choices", out var choicesValue)
                || choicesValue is not List<object?> choices
                || choices.Count == 0
                || choices[0] is not Dictionary<string, object?> choice)
            {
                return string.Empty;
            }

            return ReadString(choice, "finish_reason");
        }

        private static string ExtractRawContent(IReadOnlyDictionary<string, object?>? message)
        {
            if (message == null || !message.TryGetValue("content", out var contentValue))
            {
                return string.Empty;
            }

            return FlattenContentValue(contentValue);
        }

        private static IReadOnlyList<LlmToolCall> ExtractToolCalls(IReadOnlyDictionary<string, object?>? message)
        {
            if (message == null)
            {
                return Array.Empty<LlmToolCall>();
            }

            if (message.TryGetValue("tool_calls", out var toolCallsValue)
                && toolCallsValue is List<object?> toolCalls)
            {
                var parsedToolCalls = toolCalls
                    .OfType<Dictionary<string, object?>>()
                    .Select(ParseToolCall)
                    .Where(static toolCall => toolCall != null)
                    .Cast<LlmToolCall>()
                    .ToArray();
                if (parsedToolCalls.Length > 0)
                {
                    return parsedToolCalls;
                }
            }

            var embeddedToolCalls = ParseEmbeddedToolCalls(string.Join(
                "\n",
                new[]
                {
                    ExtractRawContent(message),
                    ExtractReasoningContent(message),
                }.Where(static text => !string.IsNullOrWhiteSpace(text))));
            return embeddedToolCalls.Count > 0
                ? embeddedToolCalls
                : Array.Empty<LlmToolCall>();
        }

        private static LlmToolCall? ParseToolCall(IReadOnlyDictionary<string, object?> payload)
        {
            if (!payload.TryGetValue("function", out var functionValue)
                || functionValue is not Dictionary<string, object?> functionPayload)
            {
                return null;
            }

            var toolName = ReadString(functionPayload, "name");
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var argumentsJson = functionPayload.TryGetValue("arguments", out var argumentsValue)
                ? argumentsValue switch
                {
                    string text => text.Trim(),
                    null => "{}",
                    _ => MiniJson.Serialize(argumentsValue),
                }
                : "{}";
            return new LlmToolCall(
                Id: string.IsNullOrWhiteSpace(ReadString(payload, "id")) ? Guid.NewGuid().ToString("N") : ReadString(payload, "id"),
                Name: toolName,
                ArgumentsJson: string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }

        private static int CalculateCompletionCharacters(
            string normalizedTtsText,
            string rawContent,
            IReadOnlyList<LlmToolCall> toolCalls)
        {
            var textLength = !string.IsNullOrWhiteSpace(normalizedTtsText)
                ? normalizedTtsText.Length
                : rawContent.Length;
            return textLength + toolCalls.Sum(toolCall => toolCall.ArgumentsJson.Length);
        }

        private static string FlattenContentValue(object? contentValue)
        {
            return contentValue switch
            {
                null => string.Empty,
                string text => text,
                List<object?> blocks => string.Join(
                    "\n",
                    blocks.Select(ExtractContentBlockText).Where(static text => !string.IsNullOrWhiteSpace(text))),
                _ => contentValue.ToString() ?? string.Empty,
            };
        }

        private static string ExtractReasoningContent(IReadOnlyDictionary<string, object?>? message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            if (message.TryGetValue("reasoning_details", out var reasoningDetailsValue))
            {
                var reasoningDetailsText = FlattenContentValue(reasoningDetailsValue);
                if (!string.IsNullOrWhiteSpace(reasoningDetailsText))
                {
                    return reasoningDetailsText;
                }
            }

            return message.TryGetValue("reasoning_content", out var reasoningContentValue)
                ? FlattenContentValue(reasoningContentValue)
                : string.Empty;
        }

        private static IReadOnlyList<LlmToolCall> ParseEmbeddedToolCalls(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return Array.Empty<LlmToolCall>();
            }

            var jsonTagMatches = Regex.Matches(
                rawText,
                "<tool_calls>(?<payload>.*?)</tool_calls>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var jsonTagCalls = jsonTagMatches
                .Cast<Match>()
                .Select(match => ParseJsonToolCallsBlock(match.Groups["payload"].Value))
                .SelectMany(static calls => calls)
                .ToArray();
            if (jsonTagCalls.Length > 0)
            {
                return jsonTagCalls;
            }

            var xmlMatch = Regex.Match(
                rawText,
                "<minimax:tool_call>(?<payload>.*?)</minimax:tool_call>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return xmlMatch.Success
                ? ParseMiniMaxXmlToolCallsBlock(xmlMatch.Groups["payload"].Value)
                : Array.Empty<LlmToolCall>();
        }

        private static IReadOnlyList<LlmToolCall> ParseJsonToolCallsBlock(string blockText)
        {
            if (string.IsNullOrWhiteSpace(blockText))
            {
                return Array.Empty<LlmToolCall>();
            }

            return blockText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => line.StartsWith("{", StringComparison.Ordinal) && line.EndsWith("}", StringComparison.Ordinal))
                .Select(ParseJsonToolCall)
                .Where(static toolCall => toolCall != null)
                .Cast<LlmToolCall>()
                .ToArray();
        }

        private static LlmToolCall? ParseJsonToolCall(string line)
        {
            if (MiniJson.Deserialize(line) is not Dictionary<string, object?> payload)
            {
                return null;
            }

            var toolName = ReadString(payload, "name");
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var argumentsJson = payload.TryGetValue("arguments", out var argumentsValue)
                ? MiniJson.Serialize(argumentsValue)
                : "{}";
            return new LlmToolCall(
                Id: Guid.NewGuid().ToString("N"),
                Name: toolName,
                ArgumentsJson: argumentsJson);
        }

        private static IReadOnlyList<LlmToolCall> ParseMiniMaxXmlToolCallsBlock(string blockText)
        {
            if (string.IsNullOrWhiteSpace(blockText))
            {
                return Array.Empty<LlmToolCall>();
            }

            var invokeMatches = Regex.Matches(
                blockText,
                "<invoke\\s+name=\"(?<name>[^\"]+)\">(?<body>.*?)</invoke>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return invokeMatches
                .Cast<Match>()
                .Select(match =>
                {
                    var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var parameterMatches = Regex.Matches(
                        match.Groups["body"].Value,
                        "<parameter\\s+name=\"(?<key>[^\"]+)\">(?<value>.*?)</parameter>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    foreach (Match parameterMatch in parameterMatches)
                    {
                        arguments[parameterMatch.Groups["key"].Value] = ParseMiniMaxXmlParameterValue(parameterMatch.Groups["value"].Value);
                    }

                    return new LlmToolCall(
                        Id: Guid.NewGuid().ToString("N"),
                        Name: match.Groups["name"].Value.Trim(),
                        ArgumentsJson: MiniJson.Serialize(arguments));
                })
                .Where(static toolCall => !string.IsNullOrWhiteSpace(toolCall.Name))
                .ToArray();
        }

        private static object? ParseMiniMaxXmlParameterValue(string rawValue)
        {
            var normalized = rawValue.Trim();
            if ((normalized.StartsWith("{", StringComparison.Ordinal) && normalized.EndsWith("}", StringComparison.Ordinal))
                || (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal)))
            {
                try
                {
                    return MiniJson.Deserialize(normalized);
                }
                catch
                {
                    return normalized;
                }
            }

            return normalized;
        }

        private static string ExtractContentBlockText(object? block)
        {
            return block switch
            {
                null => string.Empty,
                string text => text,
                Dictionary<string, object?> payload => string.IsNullOrWhiteSpace(ReadString(payload, "text"))
                    ? ReadString(payload, "content")
                    : ReadString(payload, "text"),
                _ => block.ToString() ?? string.Empty,
            };
        }

        private static string ReadString(IReadOnlyDictionary<string, object?> payload, string key)
        {
            return payload.TryGetValue(key, out var value)
                ? ReadString(value)
                : string.Empty;
        }

        private static string ReadString(object? value)
        {
            return value switch
            {
                null => string.Empty,
                string text => text.Trim(),
                _ => value.ToString()?.Trim() ?? string.Empty,
            };
        }

        private static string BuildResponsePreview(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            var normalized = responseText
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return normalized.Length <= 600
                ? normalized
                : $"{normalized[..600]}...";
        }
    }
}
