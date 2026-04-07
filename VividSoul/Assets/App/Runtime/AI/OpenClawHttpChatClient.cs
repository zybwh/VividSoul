#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VividSoul.Runtime;

namespace VividSoul.Runtime.AI
{
    public sealed class OpenClawHttpChatClient
    {
        public async Task<OpenClawHttpChatResponse> SendAsync(
            Uri gatewayUri,
            string token,
            string model,
            string sessionKey,
            string userMessage,
            CancellationToken cancellationToken)
        {
            if (gatewayUri == null)
            {
                throw new ArgumentNullException(nameof(gatewayUri));
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("A gateway token is required.", nameof(token));
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("A model is required.", nameof(model));
            }

            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                throw new ArgumentException("A session key is required.", nameof(sessionKey));
            }

            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new ArgumentException("A user message is required.", nameof(userMessage));
            }

            var normalizedToken = NormalizeToken(token);
            var endpoint = ResolveChatCompletionsEndpoint(gatewayUri);
            var requestJson = JsonUtility.ToJson(new ChatCompletionsRequestFile
            {
                model = model.Trim(),
                stream = false,
                messages = new[]
                {
                    new ChatCompletionsMessageFile
                    {
                        role = "user",
                        content = userMessage.Trim(),
                    },
                },
            });
            using var unityWebRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            unityWebRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            unityWebRequest.SetRequestHeader("Content-Type", "application/json");
            unityWebRequest.SetRequestHeader("Authorization", BuildAuthorizationHeaderValue(normalizedToken));
            unityWebRequest.SetRequestHeader("x-openclaw-session-key", sessionKey.Trim());

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
                Debug.LogWarning(
                    $"[OpenClawHttp] request failed code={(long)unityWebRequest.responseCode} error={unityWebRequest.error} tokenFingerprint={BuildTokenFingerprint(normalizedToken)} body={BuildPreview(responseText)}");
                throw new InvalidOperationException(BuildFailureMessage(unityWebRequest, responseText));
            }

            var responseFile = JsonUtility.FromJson<ChatCompletionsResponseFile>(responseText);
            var assistantText = ExtractAssistantText(responseFile);
            return new OpenClawHttpChatResponse(
                ResponseText: responseText,
                AssistantText: assistantText);
        }

        private static async Task AwaitOperationAsync(UnityWebRequestAsyncOperation operation, CancellationToken cancellationToken)
        {
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static string ResolveChatCompletionsEndpoint(Uri gatewayUri)
        {
            var httpScheme = string.Equals(gatewayUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
                ? "https"
                : "http";
            var path = gatewayUri.AbsolutePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "/", StringComparison.Ordinal))
            {
                path = "/v1/chat/completions";
            }
            else
            {
                path = $"{path.TrimEnd('/')}/v1/chat/completions";
            }

            var builder = new UriBuilder(gatewayUri)
            {
                Scheme = httpScheme,
                Path = path,
            };
            return builder.Uri.ToString();
        }

        private static string BuildAuthorizationHeaderValue(string normalizedToken)
        {
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                throw new UserFacingException("OpenClaw Token 为空。");
            }

            if (normalizedToken.Any(char.IsControl))
            {
                throw new UserFacingException("OpenClaw Token 包含非法控制字符，请重新粘贴后再试。");
            }

            return $"Bearer {normalizedToken}";
        }

        private static string BuildFailureMessage(UnityWebRequest unityWebRequest, string responseText)
        {
            var responseFile = string.IsNullOrWhiteSpace(responseText)
                ? null
                : JsonUtility.FromJson<ChatCompletionsResponseFile>(responseText);
            var responseError = responseFile?.error?.message;
            if (!string.IsNullOrWhiteSpace(responseError))
            {
                return responseError.Trim();
            }

            if (!string.IsNullOrWhiteSpace(unityWebRequest.error))
            {
                return unityWebRequest.error.Trim();
            }

            return $"HTTP {(long)unityWebRequest.responseCode}";
        }

        private static string ExtractAssistantText(ChatCompletionsResponseFile? responseFile)
        {
            var choice = responseFile?.choices != null && responseFile.choices.Length > 0
                ? responseFile.choices[0]
                : null;
            return choice?.message?.content?.Trim() ?? string.Empty;
        }

        private static string BuildPreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return normalized.Length <= 180
                ? normalized
                : $"{normalized[..180]}...";
        }

        private static string NormalizeToken(string token)
        {
            var normalized = token.Trim().Trim('"', '\'');
            if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[7..].Trim();
            }

            return normalized
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        private static string BuildTokenFingerprint(string normalizedToken)
        {
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                return "<empty>";
            }

            var hash = ComputeSha256Hex(normalizedToken);
            var suffix = normalizedToken.Length <= 4 ? normalizedToken : normalizedToken[^4..];
            return $"sha256:{hash[..12]} suffix:{suffix}";
        }

        private static string ComputeSha256Hex(string value)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(hashBytes.Length * 2);
            foreach (var hashByte in hashBytes)
            {
                builder.Append(hashByte.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class ChatCompletionsRequestFile
        {
            public string model = string.Empty;
            public bool stream;
            public ChatCompletionsMessageFile[] messages = Array.Empty<ChatCompletionsMessageFile>();
        }

        [Serializable]
        private sealed class ChatCompletionsMessageFile
        {
            public string role = "user";
            public string content = string.Empty;
        }

        [Serializable]
        private sealed class ChatCompletionsResponseFile
        {
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

    public sealed record OpenClawHttpChatResponse(
        string ResponseText,
        string AssistantText);
}
