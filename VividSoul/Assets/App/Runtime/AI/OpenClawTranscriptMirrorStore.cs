#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public sealed class OpenClawTranscriptMirrorStore
    {
        private const string DirectoryName = "ai/openclaw/transcripts";
        private readonly string transcriptsDirectoryPath;

        public OpenClawTranscriptMirrorStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            transcriptsDirectoryPath = Path.Combine(rootDirectory, DirectoryName);
        }

        public ChatMessage[] Load(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                throw new ArgumentException("A session key is required.", nameof(sessionKey));
            }

            var path = ResolvePath(sessionKey);
            if (!File.Exists(path))
            {
                return Array.Empty<ChatMessage>();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<ChatMessage>();
            }

            var file = JsonUtility.FromJson<OpenClawTranscriptFile>(json);
            return file?.messages != null
                ? file.messages.Select(static message => message.ToData()).ToArray()
                : Array.Empty<ChatMessage>();
        }

        public void Append(string sessionKey, ChatMessage message)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                throw new ArgumentException("A session key is required.", nameof(sessionKey));
            }

            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var existing = Load(sessionKey)
                .Where(existingMessage => !string.Equals(existingMessage.Id, message.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            existing.Add(message);
            existing.Sort(static (left, right) => left.CreatedAt.CompareTo(right.CreatedAt));

            Directory.CreateDirectory(transcriptsDirectoryPath);
            var file = new OpenClawTranscriptFile
            {
                messages = existing.Select(static item => OpenClawTranscriptMessageFile.FromData(item)).ToArray(),
            };
            File.WriteAllText(ResolvePath(sessionKey), JsonUtility.ToJson(file, true));
        }

        private string ResolvePath(string sessionKey)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var safeName = new string(sessionKey
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());
            return Path.Combine(transcriptsDirectoryPath, $"{safeName}.json");
        }

        [Serializable]
        private sealed class OpenClawTranscriptFile
        {
            public OpenClawTranscriptMessageFile[] messages = Array.Empty<OpenClawTranscriptMessageFile>();
        }

        [Serializable]
        private sealed class OpenClawTranscriptMessageFile
        {
            public string id = string.Empty;
            public string sessionId = string.Empty;
            public int role;
            public string text = string.Empty;
            public string createdAtUtc = string.Empty;
            public int source;

            public ChatMessage ToData()
            {
                var createdAt = DateTimeOffset.TryParse(createdAtUtc, out var parsedCreatedAt)
                    ? parsedCreatedAt
                    : DateTimeOffset.UtcNow;
                return new ChatMessage(
                    Id: id ?? string.Empty,
                    SessionId: sessionId ?? string.Empty,
                    Role: (ChatRole)role,
                    Text: text ?? string.Empty,
                    CreatedAt: createdAt,
                    Source: (ChatInvocationSource)source);
            }

            public static OpenClawTranscriptMessageFile FromData(ChatMessage data)
            {
                return new OpenClawTranscriptMessageFile
                {
                    id = data.Id,
                    sessionId = data.SessionId,
                    role = (int)data.Role,
                    text = data.Text,
                    createdAtUtc = data.CreatedAt.ToString("O"),
                    source = (int)data.Source,
                };
            }
        }
    }
}
