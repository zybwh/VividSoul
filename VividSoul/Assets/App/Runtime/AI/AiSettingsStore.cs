#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public interface IAiSettingsStore
    {
        AiSettingsData Load();

        void Save(AiSettingsData settings);
    }

    public sealed class AiSettingsStore : IAiSettingsStore
    {
        private const string DirectoryName = "ai";
        private const string FileName = "ai-settings.json";
        private const string DefaultProviderId = "default-openai";
        private readonly string filePath;

        public AiSettingsStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            filePath = Path.Combine(rootDirectory, DirectoryName, FileName);
        }

        public AiSettingsData Load()
        {
            if (!File.Exists(filePath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefault();
            }

            var file = JsonUtility.FromJson<AiSettingsFile>(json);
            return Normalize(file != null ? file.ToData() : CreateDefault());
        }

        public void Save(AiSettingsData settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("A valid AI settings directory is required.");
            }

            Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(AiSettingsFile.FromData(Normalize(settings)), true);
            File.WriteAllText(filePath, json);
        }

        private static AiSettingsData CreateDefault()
        {
            return new AiSettingsData(
                ActiveProviderId: DefaultProviderId,
                GlobalSystemPrompt: "You are the VividSoul desktop mate. Keep replies concise, warm, and suitable for an always-on desktop companion.",
                Temperature: 0.8f,
                MaxOutputTokens: 256,
                EnableStreaming: true,
                EnableProactiveMessages: false,
                ProactiveMinIntervalMinutes: 10f,
                ProactiveMaxIntervalMinutes: 30f,
                MemoryWindowTurns: 12,
                SummaryThreshold: 24,
                EnableTts: false,
                ProviderProfiles: new[]
                {
                    new LlmProviderProfile(
                        Id: DefaultProviderId,
                        DisplayName: "OpenAI Compatible",
                        ProviderType: LlmProviderType.OpenAiCompatible,
                        BaseUrl: "https://api.openai.com/v1",
                        Model: "gpt-4.1-mini",
                        Enabled: true),
                });
        }

        private static AiSettingsData Normalize(AiSettingsData settings)
        {
            var profiles = settings.ProviderProfiles
                .Where(static profile => profile != null)
                .Select(static profile => NormalizeProfile(profile))
                .Where(static profile => !string.IsNullOrWhiteSpace(profile.Id))
                .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();

            if (profiles.Length == 0)
            {
                return CreateDefault();
            }

            var activeProviderId = profiles.Any(profile => string.Equals(profile.Id, settings.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                ? settings.ActiveProviderId.Trim()
                : profiles[0].Id;

            var proactiveMin = Mathf.Max(1f, settings.ProactiveMinIntervalMinutes);
            var proactiveMax = Mathf.Max(proactiveMin, settings.ProactiveMaxIntervalMinutes);

            return new AiSettingsData(
                ActiveProviderId: activeProviderId,
                GlobalSystemPrompt: settings.GlobalSystemPrompt?.Trim() ?? string.Empty,
                Temperature: Mathf.Clamp(settings.Temperature, 0f, 2f),
                MaxOutputTokens: Mathf.Max(32, settings.MaxOutputTokens),
                EnableStreaming: settings.EnableStreaming,
                EnableProactiveMessages: settings.EnableProactiveMessages,
                ProactiveMinIntervalMinutes: proactiveMin,
                ProactiveMaxIntervalMinutes: proactiveMax,
                MemoryWindowTurns: Mathf.Max(2, settings.MemoryWindowTurns),
                SummaryThreshold: Mathf.Max(4, settings.SummaryThreshold),
                EnableTts: settings.EnableTts,
                ProviderProfiles: profiles);
        }

        private static LlmProviderProfile NormalizeProfile(LlmProviderProfile profile)
        {
            var normalizedId = string.IsNullOrWhiteSpace(profile.Id)
                ? Guid.NewGuid().ToString("N")
                : profile.Id.Trim();
            var normalizedDisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? normalizedId
                : profile.DisplayName.Trim();
            var normalizedBaseUrl = profile.BaseUrl?.Trim() ?? string.Empty;
            var normalizedModel = profile.Model?.Trim() ?? string.Empty;
            var normalizedOpenClawGatewayWsUrl = profile.OpenClawGatewayWsUrl?.Trim() ?? string.Empty;
            var normalizedOpenClawAgentId = string.IsNullOrWhiteSpace(profile.OpenClawAgentId)
                ? "main"
                : profile.OpenClawAgentId.Trim();
            var normalizedOpenClawSessionKeyTemplate = profile.OpenClawSessionKeyTemplate?.Trim() ?? string.Empty;
            var normalizedProviderType = NormalizeProviderType(
                profile.ProviderType,
                normalizedDisplayName,
                normalizedBaseUrl,
                normalizedModel);

            return profile with
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                ProviderType = normalizedProviderType,
                BaseUrl = normalizedBaseUrl,
                Model = normalizedModel,
                OpenClawGatewayWsUrl = normalizedOpenClawGatewayWsUrl,
                OpenClawAgentId = normalizedOpenClawAgentId,
                OpenClawSessionMode = profile.OpenClawSessionMode,
                OpenClawSessionKeyTemplate = normalizedOpenClawSessionKeyTemplate,
                OpenClawAutoConnect = profile.OpenClawAutoConnect,
                OpenClawAutoReconnect = profile.OpenClawAutoReconnect,
                OpenClawReceiveProactiveMessages = profile.OpenClawReceiveProactiveMessages,
                OpenClawMirrorTranscriptLocally = profile.OpenClawMirrorTranscriptLocally,
                OpenClawEnableBubbleForIncoming = profile.OpenClawEnableBubbleForIncoming,
                OpenClawEnableTtsForIncoming = profile.OpenClawEnableTtsForIncoming,
                MiniMaxTtsModel = NormalizeMiniMaxTtsModel(normalizedProviderType, profile.MiniMaxTtsModel),
                MiniMaxTtsVoiceId = NormalizeMiniMaxTtsVoiceId(normalizedProviderType, profile.MiniMaxTtsVoiceId),
            };
        }

        private static string NormalizeMiniMaxTtsModel(LlmProviderType providerType, string value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return providerType == LlmProviderType.MiniMax && string.IsNullOrWhiteSpace(normalized)
                ? "speech-02-turbo"
                : normalized;
        }

        private static string NormalizeMiniMaxTtsVoiceId(LlmProviderType providerType, string value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return providerType == LlmProviderType.MiniMax && string.IsNullOrWhiteSpace(normalized)
                ? "Chinese (Mandarin)_Soft_Girl"
                : normalized;
        }

        private static LlmProviderType NormalizeProviderType(
            LlmProviderType providerType,
            string displayName,
            string baseUrl,
            string model)
        {
            if (providerType != LlmProviderType.OpenAiCompatible)
            {
                return providerType;
            }

            var isMiniMax = baseUrl.Contains("minimax", StringComparison.OrdinalIgnoreCase)
                            || displayName.Contains("minimax", StringComparison.OrdinalIgnoreCase)
                            || model.Contains("minimax", StringComparison.OrdinalIgnoreCase);
            return isMiniMax ? LlmProviderType.MiniMax : providerType;
        }

        [Serializable]
        private sealed class AiSettingsFile
        {
            public string activeProviderId = DefaultProviderId;
            public string globalSystemPrompt = string.Empty;
            public float temperature = 0.8f;
            public int maxOutputTokens = 256;
            public bool enableStreaming = true;
            public bool enableProactiveMessages;
            public float proactiveMinIntervalMinutes = 10f;
            public float proactiveMaxIntervalMinutes = 30f;
            public int memoryWindowTurns = 12;
            public int summaryThreshold = 24;
            public bool enableTts;
            public LlmProviderProfileFile[] providerProfiles = Array.Empty<LlmProviderProfileFile>();

            public AiSettingsData ToData()
            {
                return new AiSettingsData(
                    ActiveProviderId: activeProviderId ?? string.Empty,
                    GlobalSystemPrompt: globalSystemPrompt ?? string.Empty,
                    Temperature: temperature,
                    MaxOutputTokens: maxOutputTokens,
                    EnableStreaming: enableStreaming,
                    EnableProactiveMessages: enableProactiveMessages,
                    ProactiveMinIntervalMinutes: proactiveMinIntervalMinutes,
                    ProactiveMaxIntervalMinutes: proactiveMaxIntervalMinutes,
                    MemoryWindowTurns: memoryWindowTurns,
                    SummaryThreshold: summaryThreshold,
                    EnableTts: enableTts,
                    ProviderProfiles: providerProfiles != null
                        ? providerProfiles.Select(static profile => profile.ToData()).ToArray()
                        : Array.Empty<LlmProviderProfile>());
            }

            public static AiSettingsFile FromData(AiSettingsData data)
            {
                return new AiSettingsFile
                {
                    activeProviderId = data.ActiveProviderId,
                    globalSystemPrompt = data.GlobalSystemPrompt,
                    temperature = data.Temperature,
                    maxOutputTokens = data.MaxOutputTokens,
                    enableStreaming = data.EnableStreaming,
                    enableProactiveMessages = data.EnableProactiveMessages,
                    proactiveMinIntervalMinutes = data.ProactiveMinIntervalMinutes,
                    proactiveMaxIntervalMinutes = data.ProactiveMaxIntervalMinutes,
                    memoryWindowTurns = data.MemoryWindowTurns,
                    summaryThreshold = data.SummaryThreshold,
                    enableTts = data.EnableTts,
                    providerProfiles = data.ProviderProfiles.Select(static profile => LlmProviderProfileFile.FromData(profile)).ToArray(),
                };
            }
        }

        [Serializable]
        private sealed class LlmProviderProfileFile
        {
            public string id = string.Empty;
            public string displayName = string.Empty;
            public int providerType;
            public string baseUrl = string.Empty;
            public string model = string.Empty;
            public bool enabled = true;
            public string openClawGatewayWsUrl = string.Empty;
            public string openClawAgentId = "main";
            public int openClawSessionMode;
            public string openClawSessionKeyTemplate = string.Empty;
            public bool openClawAutoConnect = true;
            public bool openClawAutoReconnect = true;
            public bool openClawReceiveProactiveMessages = true;
            public bool openClawMirrorTranscriptLocally = true;
            public bool openClawEnableBubbleForIncoming = true;
            public bool openClawEnableTtsForIncoming;
            public string miniMaxTtsModel = string.Empty;
            public string miniMaxTtsVoiceId = string.Empty;

            public LlmProviderProfile ToData()
            {
                return new LlmProviderProfile(
                    Id: id ?? string.Empty,
                    DisplayName: displayName ?? string.Empty,
                    ProviderType: (LlmProviderType)providerType,
                    BaseUrl: baseUrl ?? string.Empty,
                    Model: model ?? string.Empty,
                    Enabled: enabled,
                    OpenClawGatewayWsUrl: openClawGatewayWsUrl ?? string.Empty,
                    OpenClawAgentId: string.IsNullOrWhiteSpace(openClawAgentId) ? "main" : openClawAgentId,
                    OpenClawSessionMode: (OpenClawSessionMode)openClawSessionMode,
                    OpenClawSessionKeyTemplate: openClawSessionKeyTemplate ?? string.Empty,
                    OpenClawAutoConnect: openClawAutoConnect,
                    OpenClawAutoReconnect: openClawAutoReconnect,
                    OpenClawReceiveProactiveMessages: openClawReceiveProactiveMessages,
                    OpenClawMirrorTranscriptLocally: openClawMirrorTranscriptLocally,
                    OpenClawEnableBubbleForIncoming: openClawEnableBubbleForIncoming,
                    OpenClawEnableTtsForIncoming: openClawEnableTtsForIncoming,
                    MiniMaxTtsModel: miniMaxTtsModel ?? string.Empty,
                    MiniMaxTtsVoiceId: miniMaxTtsVoiceId ?? string.Empty);
            }

            public static LlmProviderProfileFile FromData(LlmProviderProfile data)
            {
                return new LlmProviderProfileFile
                {
                    id = data.Id,
                    displayName = data.DisplayName,
                    providerType = (int)data.ProviderType,
                    baseUrl = data.BaseUrl,
                    model = data.Model,
                    enabled = data.Enabled,
                    openClawGatewayWsUrl = data.OpenClawGatewayWsUrl,
                    openClawAgentId = data.OpenClawAgentId,
                    openClawSessionMode = (int)data.OpenClawSessionMode,
                    openClawSessionKeyTemplate = data.OpenClawSessionKeyTemplate,
                    openClawAutoConnect = data.OpenClawAutoConnect,
                    openClawAutoReconnect = data.OpenClawAutoReconnect,
                    openClawReceiveProactiveMessages = data.OpenClawReceiveProactiveMessages,
                    openClawMirrorTranscriptLocally = data.OpenClawMirrorTranscriptLocally,
                    openClawEnableBubbleForIncoming = data.OpenClawEnableBubbleForIncoming,
                    openClawEnableTtsForIncoming = data.OpenClawEnableTtsForIncoming,
                    miniMaxTtsModel = data.MiniMaxTtsModel,
                    miniMaxTtsVoiceId = data.MiniMaxTtsVoiceId,
                };
            }
        }
    }
}
