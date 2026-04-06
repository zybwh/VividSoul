#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public interface IChatSessionStore
    {
        ChatSessionData Load(string sessionId, string characterFingerprint);

        void Save(ChatSessionData session);
    }

    public sealed class ChatSessionStore : IChatSessionStore
    {
        private const string DirectoryName = "ai/sessions";
        private readonly string sessionsDirectoryPath;

        public ChatSessionStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            sessionsDirectoryPath = Path.Combine(rootDirectory, DirectoryName);
        }

        public ChatSessionData Load(string sessionId, string characterFingerprint)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A session id is required.", nameof(sessionId));
            }

            var filePath = ResolveFilePath(sessionId);
            if (!File.Exists(filePath))
            {
                return CreateDefault(sessionId, characterFingerprint);
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefault(sessionId, characterFingerprint);
            }

            var file = JsonUtility.FromJson<ChatSessionFile>(json);
            return Normalize(file != null ? file.ToData() : CreateDefault(sessionId, characterFingerprint), sessionId, characterFingerprint);
        }

        public void Save(ChatSessionData session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var normalized = Normalize(session, session.SessionId, session.CharacterFingerprint);
            Directory.CreateDirectory(sessionsDirectoryPath);
            var json = JsonUtility.ToJson(ChatSessionFile.FromData(normalized), true);
            File.WriteAllText(ResolveFilePath(normalized.SessionId), json);
        }

        private ChatSessionData CreateDefault(string sessionId, string characterFingerprint)
        {
            return new ChatSessionData(
                SessionId: sessionId.Trim(),
                CharacterFingerprint: characterFingerprint?.Trim() ?? string.Empty,
                Messages: Array.Empty<ChatMessage>());
        }

        private static ChatSessionData Normalize(ChatSessionData session, string fallbackSessionId, string fallbackCharacterFingerprint)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(session.SessionId)
                ? fallbackSessionId.Trim()
                : session.SessionId.Trim();
            var normalizedCharacterFingerprint = string.IsNullOrWhiteSpace(session.CharacterFingerprint)
                ? fallbackCharacterFingerprint?.Trim() ?? string.Empty
                : session.CharacterFingerprint.Trim();
            var normalizedMessages = session.Messages
                .Where(static message => message != null)
                .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
                .Select(message => NormalizeMessage(message, normalizedSessionId))
                .OrderBy(message => message.CreatedAt)
                .ToArray();

            return new ChatSessionData(
                SessionId: normalizedSessionId,
                CharacterFingerprint: normalizedCharacterFingerprint,
                Messages: normalizedMessages);
        }

        private static ChatMessage NormalizeMessage(ChatMessage message, string sessionId)
        {
            return message with
            {
                Id = string.IsNullOrWhiteSpace(message.Id) ? Guid.NewGuid().ToString("N") : message.Id.Trim(),
                SessionId = string.IsNullOrWhiteSpace(message.SessionId) ? sessionId : message.SessionId.Trim(),
                Text = message.Text.Trim(),
            };
        }

        private string ResolveFilePath(string sessionId)
        {
            var safeFileName = ToSafeFileName(sessionId.Trim());
            return Path.Combine(sessionsDirectoryPath, $"{safeFileName}.json");
        }

        private static string ToSafeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var normalizedCharacters = value
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray();
            var normalized = new string(normalizedCharacters).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "default-session" : normalized;
        }

        [Serializable]
        private sealed class ChatSessionFile
        {
            public string sessionId = string.Empty;
            public string characterFingerprint = string.Empty;
            public ChatMessageFile[] messages = Array.Empty<ChatMessageFile>();

            public ChatSessionData ToData()
            {
                return new ChatSessionData(
                    SessionId: sessionId ?? string.Empty,
                    CharacterFingerprint: characterFingerprint ?? string.Empty,
                    Messages: messages != null
                        ? messages.Select(static message => message.ToData()).ToArray()
                        : Array.Empty<ChatMessage>());
            }

            public static ChatSessionFile FromData(ChatSessionData data)
            {
                return new ChatSessionFile
                {
                    sessionId = data.SessionId,
                    characterFingerprint = data.CharacterFingerprint,
                    messages = data.Messages.Select(static message => ChatMessageFile.FromData(message)).ToArray(),
                };
            }
        }

        [Serializable]
        private sealed class ChatMessageFile
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

            public static ChatMessageFile FromData(ChatMessage data)
            {
                return new ChatMessageFile
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

    public sealed record ChatSessionData(
        string SessionId,
        string CharacterFingerprint,
        System.Collections.Generic.IReadOnlyList<ChatMessage> Messages);
}
