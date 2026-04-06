#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public interface ILlmUsageStatsStore
    {
        LlmUsageStatsData Load();

        void Reset();

        void RecordSuccess(string providerId, string model, long latencyMs, int promptCharacters, int completionCharacters);

        void RecordFailure(string providerId, string model, string errorMessage, long latencyMs, int promptCharacters);
    }

    public sealed class LlmUsageStatsStore : ILlmUsageStatsStore
    {
        private const string DirectoryName = "ai";
        private const string FileName = "llm-usage-stats.json";
        private readonly string filePath;

        public LlmUsageStatsStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            filePath = Path.Combine(rootDirectory, DirectoryName, FileName);
        }

        public LlmUsageStatsData Load()
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

            var file = JsonUtility.FromJson<LlmUsageStatsFile>(json);
            return file != null ? file.ToData() : CreateDefault();
        }

        public void Reset()
        {
            Save(CreateDefault());
        }

        public void RecordSuccess(string providerId, string model, long latencyMs, int promptCharacters, int completionCharacters)
        {
            var current = Load();
            Save(current with
            {
                TotalRequestCount = current.TotalRequestCount + 1,
                SuccessfulRequestCount = current.SuccessfulRequestCount + 1,
                TotalLatencyMs = current.TotalLatencyMs + Math.Max(0L, latencyMs),
                TotalPromptCharacters = current.TotalPromptCharacters + Math.Max(0, promptCharacters),
                TotalCompletionCharacters = current.TotalCompletionCharacters + Math.Max(0, completionCharacters),
                LastProviderId = providerId?.Trim() ?? string.Empty,
                LastModel = model?.Trim() ?? string.Empty,
                LastRequestAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                LastErrorMessage = string.Empty,
            });
        }

        public void RecordFailure(string providerId, string model, string errorMessage, long latencyMs, int promptCharacters)
        {
            var current = Load();
            Save(current with
            {
                TotalRequestCount = current.TotalRequestCount + 1,
                FailedRequestCount = current.FailedRequestCount + 1,
                TotalLatencyMs = current.TotalLatencyMs + Math.Max(0L, latencyMs),
                TotalPromptCharacters = current.TotalPromptCharacters + Math.Max(0, promptCharacters),
                LastProviderId = providerId?.Trim() ?? string.Empty,
                LastModel = model?.Trim() ?? string.Empty,
                LastRequestAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                LastErrorMessage = errorMessage?.Trim() ?? string.Empty,
            });
        }

        private void Save(LlmUsageStatsData stats)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("A valid LLM usage stats directory is required.");
            }

            Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(LlmUsageStatsFile.FromData(stats), true);
            File.WriteAllText(filePath, json);
        }

        private static LlmUsageStatsData CreateDefault()
        {
            return new LlmUsageStatsData(
                TotalRequestCount: 0,
                SuccessfulRequestCount: 0,
                FailedRequestCount: 0,
                TotalLatencyMs: 0,
                TotalPromptCharacters: 0,
                TotalCompletionCharacters: 0,
                LastProviderId: string.Empty,
                LastModel: string.Empty,
                LastRequestAtUtc: string.Empty,
                LastErrorMessage: string.Empty);
        }

        [Serializable]
        private sealed class LlmUsageStatsFile
        {
            public int totalRequestCount;
            public int successfulRequestCount;
            public int failedRequestCount;
            public long totalLatencyMs;
            public long totalPromptCharacters;
            public long totalCompletionCharacters;
            public string lastProviderId = string.Empty;
            public string lastModel = string.Empty;
            public string lastRequestAtUtc = string.Empty;
            public string lastErrorMessage = string.Empty;

            public LlmUsageStatsData ToData()
            {
                return new LlmUsageStatsData(
                    TotalRequestCount: totalRequestCount,
                    SuccessfulRequestCount: successfulRequestCount,
                    FailedRequestCount: failedRequestCount,
                    TotalLatencyMs: totalLatencyMs,
                    TotalPromptCharacters: totalPromptCharacters,
                    TotalCompletionCharacters: totalCompletionCharacters,
                    LastProviderId: lastProviderId ?? string.Empty,
                    LastModel: lastModel ?? string.Empty,
                    LastRequestAtUtc: lastRequestAtUtc ?? string.Empty,
                    LastErrorMessage: lastErrorMessage ?? string.Empty);
            }

            public static LlmUsageStatsFile FromData(LlmUsageStatsData data)
            {
                return new LlmUsageStatsFile
                {
                    totalRequestCount = data.TotalRequestCount,
                    successfulRequestCount = data.SuccessfulRequestCount,
                    failedRequestCount = data.FailedRequestCount,
                    totalLatencyMs = data.TotalLatencyMs,
                    totalPromptCharacters = data.TotalPromptCharacters,
                    totalCompletionCharacters = data.TotalCompletionCharacters,
                    lastProviderId = data.LastProviderId,
                    lastModel = data.LastModel,
                    lastRequestAtUtc = data.LastRequestAtUtc,
                    lastErrorMessage = data.LastErrorMessage,
                };
            }
        }
    }
}
