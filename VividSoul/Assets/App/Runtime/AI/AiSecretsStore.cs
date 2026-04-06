#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public interface IAiSecretsStore
    {
        string LoadApiKey(string providerId);

        void SaveApiKey(string providerId, string apiKey);

        void RemoveApiKey(string providerId);
    }

    public sealed class AiSecretsStore : IAiSecretsStore
    {
        private const string DirectoryName = "ai";
        private const string FileName = "ai-secrets.json";
        private readonly string filePath;

        public AiSecretsStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            filePath = Path.Combine(rootDirectory, DirectoryName, FileName);
        }

        public string LoadApiKey(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("A provider id is required.", nameof(providerId));
            }

            var data = LoadData();
            var entry = data.ProviderSecrets.FirstOrDefault(secret =>
                string.Equals(secret.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            return entry?.ApiKey ?? string.Empty;
        }

        public void SaveApiKey(string providerId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("A provider id is required.", nameof(providerId));
            }

            var data = LoadData();
            var normalizedProviderId = providerId.Trim();
            var normalizedApiKey = NormalizeApiKey(apiKey);
            var index = Array.FindIndex(data.ProviderSecrets, secret =>
                string.Equals(secret.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(normalizedApiKey))
            {
                if (index >= 0)
                {
                    data.ProviderSecrets = data.ProviderSecrets
                        .Where(secret => !string.Equals(secret.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }
            }
            else if (index >= 0)
            {
                data.ProviderSecrets[index] = new ProviderSecretFile
                {
                    ProviderId = normalizedProviderId,
                    ApiKey = normalizedApiKey,
                };
            }
            else
            {
                data.ProviderSecrets = data.ProviderSecrets
                    .Append(new ProviderSecretFile
                    {
                        ProviderId = normalizedProviderId,
                        ApiKey = normalizedApiKey,
                    })
                    .ToArray();
            }

            SaveData(data);
        }

        public void RemoveApiKey(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("A provider id is required.", nameof(providerId));
            }

            var data = LoadData();
            data.ProviderSecrets = data.ProviderSecrets
                .Where(secret => !string.Equals(secret.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            SaveData(data);
        }

        private SecretsFile LoadData()
        {
            if (!File.Exists(filePath))
            {
                return new SecretsFile();
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SecretsFile();
            }

            return JsonUtility.FromJson<SecretsFile>(json) ?? new SecretsFile();
        }

        private void SaveData(SecretsFile data)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("A valid AI secrets directory is required.");
            }

            Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(filePath, json);
        }

        private static string NormalizeApiKey(string? apiKey)
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
            return normalized;
        }

        [Serializable]
        private sealed class SecretsFile
        {
            public ProviderSecretFile[] ProviderSecrets = Array.Empty<ProviderSecretFile>();
        }

        [Serializable]
        private sealed class ProviderSecretFile
        {
            public string ProviderId = string.Empty;
            public string ApiKey = string.Empty;
        }
    }
}
