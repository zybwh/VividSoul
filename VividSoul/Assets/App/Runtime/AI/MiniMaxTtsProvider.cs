#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using VividSoul.Runtime;

namespace VividSoul.Runtime.AI
{
    public sealed class MiniMaxTtsProvider : ITtsProvider
    {
        private const int RequestTimeoutSeconds = 45;
        private const string DefaultTtsModel = "speech-02-turbo";
        private const string DefaultVoiceId = "Chinese (Mandarin)_Soft_Girl";
        private const string DefaultOutputFormat = "url";
        private const string DefaultAudioFormat = "wav";
        private const int DefaultSampleRate = 32000;
        private const int DefaultChannel = 1;
        private const float DefaultSpeed = 1f;
        private const int DefaultPitch = 0;

        public LlmProviderType ProviderType => LlmProviderType.MiniMax;

        public async Task<TtsSynthesisResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.ProviderProfile == null)
            {
                throw new ArgumentException("A provider profile is required.", nameof(request));
            }

            if (request.ProviderProfile.ProviderType != LlmProviderType.MiniMax)
            {
                throw new UserFacingException("当前 TTS 只接通了 MiniMax Provider。");
            }

            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                throw new UserFacingException("MiniMax 的 API Key 为空，无法发起 TTS。");
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                throw new ArgumentException("A tts text is required.", nameof(request));
            }

            var endpoint = ResolveTtsEndpoint(request.ProviderProfile.BaseUrl);
            var requestJson = BuildRequestJson(request);
            using var unityWebRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            unityWebRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(requestJson));
            unityWebRequest.timeout = RequestTimeoutSeconds;
            unityWebRequest.SetRequestHeader("Content-Type", "application/json");
            unityWebRequest.SetRequestHeader("Authorization", BuildAuthorizationHeaderValue(request.ApiKey));

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

            return ParseSynthesisResult(responseText);
        }

        private static async Task AwaitOperationAsync(UnityWebRequestAsyncOperation operation, CancellationToken cancellationToken)
        {
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static string ResolveTtsEndpoint(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new UserFacingException("MiniMax Provider 缺少 API URL。");
            }

            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            {
                throw new UserFacingException("MiniMax Provider 的 API URL 不是合法地址。");
            }

            var path = uri.AbsolutePath ?? string.Empty;
            var versionIndex = path.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
            var normalizedPath = versionIndex >= 0
                ? $"{path[..(versionIndex + 3)].TrimEnd('/')}/t2a_v2"
                : "/v1/t2a_v2";
            var builder = new UriBuilder(uri)
            {
                Path = normalizedPath,
                Query = string.Empty,
            };
            return builder.Uri.ToString();
        }

        private static string BuildAuthorizationHeaderValue(string apiKey)
        {
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
                throw new UserFacingException("MiniMax 的 API Key 为空，无法发起 TTS。");
            }

            if (normalized.Any(char.IsControl))
            {
                throw new UserFacingException("MiniMax 的 API Key 包含非法控制字符，请重新粘贴后再试。");
            }

            return $"Bearer {normalized}";
        }

        private static string BuildRequestJson(TtsRequest request)
        {
            var normalizedTtsModel = string.IsNullOrWhiteSpace(request.ProviderProfile.MiniMaxTtsModel)
                ? DefaultTtsModel
                : request.ProviderProfile.MiniMaxTtsModel.Trim();
            var normalizedVoiceId = string.IsNullOrWhiteSpace(request.PreferredVoiceId)
                ? NormalizeVoiceId(request.ProviderProfile)
                : request.PreferredVoiceId.Trim();
            var normalizedVolume = Math.Clamp(request.Volume, 0.1f, 10f);
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["model"] = normalizedTtsModel,
                ["text"] = request.Text.Trim(),
                ["stream"] = false,
                ["output_format"] = DefaultOutputFormat,
                ["voice_setting"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["voice_id"] = normalizedVoiceId,
                    ["speed"] = DefaultSpeed,
                    ["vol"] = normalizedVolume,
                    ["pitch"] = DefaultPitch,
                },
                ["audio_setting"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["format"] = DefaultAudioFormat,
                    ["sample_rate"] = DefaultSampleRate,
                    ["channel"] = DefaultChannel,
                },
            };
            return MiniJson.Serialize(payload);
        }

        private static string NormalizeVoiceId(LlmProviderProfile profile)
        {
            return string.IsNullOrWhiteSpace(profile.MiniMaxTtsVoiceId)
                ? DefaultVoiceId
                : profile.MiniMaxTtsVoiceId.Trim();
        }

        private static TtsSynthesisResult ParseSynthesisResult(string responseText)
        {
            if (MiniJson.Deserialize(responseText) is not Dictionary<string, object?> root)
            {
                throw new UserFacingException("MiniMax TTS 返回了无法解析的响应。");
            }

            var baseResponse = ReadDictionary(root, "base_resp");
            var statusCode = ReadLong(baseResponse, "status_code");
            if (statusCode != 0)
            {
                var statusMessage = ReadString(baseResponse, "status_msg");
                throw new UserFacingException(string.IsNullOrWhiteSpace(statusMessage)
                    ? $"MiniMax TTS 请求失败：状态码 {statusCode.ToString(CultureInfo.InvariantCulture)}"
                    : $"MiniMax TTS 请求失败：{statusMessage}");
            }

            var data = ReadDictionary(root, "data");
            var audioUrl = ReadString(data, "audio");
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                throw new UserFacingException("MiniMax TTS 没有返回可播放的音频地址。");
            }

            return new TtsSynthesisResult(audioUrl.Trim(), DefaultAudioFormat);
        }

        private static string BuildFailureMessage(UnityWebRequest unityWebRequest, string responseText)
        {
            var responseError = TryReadErrorMessage(responseText);
            if (!string.IsNullOrWhiteSpace(responseError))
            {
                return $"MiniMax TTS 请求失败：{responseError}";
            }

            if (!string.IsNullOrWhiteSpace(unityWebRequest.error))
            {
                return $"MiniMax TTS 请求失败：{unityWebRequest.error.Trim()}";
            }

            return $"MiniMax TTS 请求失败：HTTP {(long)unityWebRequest.responseCode}";
        }

        private static string TryReadErrorMessage(string responseText)
        {
            try
            {
                if (MiniJson.Deserialize(responseText) is not Dictionary<string, object?> root)
                {
                    return string.Empty;
                }

                var baseResponse = ReadDictionary(root, "base_resp");
                var statusMessage = ReadString(baseResponse, "status_msg");
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    return statusMessage.Trim();
                }

                if (root.TryGetValue("error", out var errorValue) && errorValue is Dictionary<string, object?> errorPayload)
                {
                    return ReadString(errorPayload, "message");
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Dictionary<string, object?> ReadDictionary(Dictionary<string, object?> source, string key)
        {
            return source.TryGetValue(key, out var value) && value is Dictionary<string, object?> dictionary
                ? dictionary
                : new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        private static string ReadString(Dictionary<string, object?> source, string key)
        {
            return source.TryGetValue(key, out var value) && value != null
                ? value.ToString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static long ReadLong(Dictionary<string, object?> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || value == null)
            {
                return 0L;
            }

            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                double doubleValue => Convert.ToInt64(doubleValue),
                float floatValue => Convert.ToInt64(floatValue),
                string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    => parsed,
                _ => 0L,
            };
        }
    }
}
