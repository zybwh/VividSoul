#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public interface IChatSessionStore
    {
        ChatSessionData Load(string sessionId, string characterFingerprint);

        void Save(ChatSessionData session);

        void SaveCompact(ChatCompactData compact);
    }

    public sealed class ChatSessionStore : IChatSessionStore
    {
        private const string CharactersDirectoryName = "ai/local-agent/characters";
        private const string MemoryDirectoryName = "memory";
        private const string ThreadsDirectoryName = "threads";
        private const string CompactsDirectoryName = "compact";
        private const string ThreadIndexFileName = "thread-index.json";
        private const string CompactIndexFileName = "compact-index.json";
        private const string LegacySessionsDirectoryName = "ai/sessions";
        private const string DefaultThreadId = "thread-0001";

        private readonly string charactersDirectoryPath;
        private readonly string legacySessionsDirectoryPath;

        public ChatSessionStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            charactersDirectoryPath = Path.Combine(rootDirectory, CharactersDirectoryName);
            legacySessionsDirectoryPath = Path.Combine(rootDirectory, LegacySessionsDirectoryName);
        }

        public ChatSessionData Load(string sessionId, string characterFingerprint)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A session id is required.", nameof(sessionId));
            }

            var normalizedSessionId = sessionId.Trim();
            var normalizedCharacterFingerprint = characterFingerprint?.Trim() ?? string.Empty;
            var threadIndexFilePath = ResolveThreadIndexFilePath(normalizedCharacterFingerprint);
            if (File.Exists(threadIndexFilePath))
            {
                var threadIndex = LoadThreadIndex(normalizedSessionId, normalizedCharacterFingerprint);
                var activeThreadId = string.IsNullOrWhiteSpace(threadIndex.activeThreadId)
                    ? DefaultThreadId
                    : threadIndex.activeThreadId.Trim();
                var messages = LoadThreadMessages(normalizedCharacterFingerprint, activeThreadId, normalizedSessionId);
                return Normalize(new ChatSessionData(
                    SessionId: threadIndex.sessionId,
                    CharacterFingerprint: threadIndex.characterFingerprint,
                    ActiveThreadId: activeThreadId,
                    Messages: messages,
                    UpdatedAt: ParseTimestamp(threadIndex.updatedAtUtc),
                    LastUserMessageAt: ParseNullableTimestamp(threadIndex.lastUserMessageAtUtc)),
                    normalizedSessionId,
                    normalizedCharacterFingerprint);
            }

            var legacySession = TryLoadLegacy(normalizedSessionId, normalizedCharacterFingerprint);
            if (legacySession != null)
            {
                Save(legacySession);
                return legacySession;
            }

            return CreateDefault(normalizedSessionId, normalizedCharacterFingerprint);
        }

        public void Save(ChatSessionData session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var normalized = Normalize(session, session.SessionId, session.CharacterFingerprint);
            EnsureMemoryDirectories(normalized.CharacterFingerprint);
            WriteThreadMessages(normalized.CharacterFingerprint, normalized.ActiveThreadId, normalized.Messages, normalized.SessionId);

            var threadIndex = LoadThreadIndex(normalized.SessionId, normalized.CharacterFingerprint);
            var existingThreadEntry = threadIndex.threads.FirstOrDefault(entry =>
                string.Equals(entry.threadId, normalized.ActiveThreadId, StringComparison.Ordinal));
            var createdAt = ParseNullableTimestamp(existingThreadEntry?.createdAtUtc) ?? ResolveCreatedAt(normalized);
            var updatedAt = normalized.UpdatedAt.ToString("O");
            var updatedThreadEntry = new ThreadIndexEntryFile
            {
                threadId = normalized.ActiveThreadId,
                createdAtUtc = createdAt.ToString("O"),
                updatedAtUtc = updatedAt,
                messageCount = normalized.Messages.Count,
            };
            threadIndex.sessionId = normalized.SessionId;
            threadIndex.characterFingerprint = normalized.CharacterFingerprint;
            threadIndex.activeThreadId = normalized.ActiveThreadId;
            threadIndex.updatedAtUtc = updatedAt;
            threadIndex.lastUserMessageAtUtc = normalized.LastUserMessageAt?.ToString("O") ?? string.Empty;
            threadIndex.threads = threadIndex.threads
                .Where(entry => entry != null && !string.Equals(entry.threadId, normalized.ActiveThreadId, StringComparison.Ordinal))
                .Append(updatedThreadEntry)
                .OrderBy(entry => ParseTimestamp(entry.createdAtUtc))
                .ToArray();

            File.WriteAllText(
                ResolveThreadIndexFilePath(normalized.CharacterFingerprint),
                JsonUtility.ToJson(threadIndex, true));

            EnsureCompactIndexExists(normalized.SessionId, normalized.CharacterFingerprint);
        }

        public void SaveCompact(ChatCompactData compact)
        {
            if (compact == null)
            {
                throw new ArgumentNullException(nameof(compact));
            }

            var normalized = NormalizeCompact(compact);
            EnsureMemoryDirectories(normalized.CharacterFingerprint);
            File.WriteAllText(
                ResolveCompactFilePath(normalized.CharacterFingerprint, normalized.CompactId),
                normalized.Content.EndsWith("\n", StringComparison.Ordinal) ? normalized.Content : $"{normalized.Content}\n");

            var compactIndex = LoadCompactIndex(normalized.SessionId, normalized.CharacterFingerprint);
            var compactEntry = new CompactIndexEntryFile
            {
                compactId = normalized.CompactId,
                threadId = normalized.ThreadId,
                startedAtUtc = normalized.StartedAt.ToString("O"),
                endedAtUtc = normalized.EndedAt.ToString("O"),
                messageCount = normalized.MessageCount,
            };
            compactIndex.sessionId = normalized.SessionId;
            compactIndex.characterFingerprint = normalized.CharacterFingerprint;
            compactIndex.compacts = compactIndex.compacts
                .Where(entry => entry != null && !string.Equals(entry.compactId, normalized.CompactId, StringComparison.Ordinal))
                .Append(compactEntry)
                .OrderBy(entry => ParseTimestamp(entry.startedAtUtc))
                .ToArray();

            File.WriteAllText(
                ResolveCompactIndexFilePath(normalized.CharacterFingerprint),
                JsonUtility.ToJson(compactIndex, true));
        }

        private ChatSessionData CreateDefault(string sessionId, string characterFingerprint)
        {
            var now = DateTimeOffset.UtcNow;
            return new ChatSessionData(
                SessionId: sessionId.Trim(),
                CharacterFingerprint: characterFingerprint?.Trim() ?? string.Empty,
                ActiveThreadId: DefaultThreadId,
                Messages: Array.Empty<ChatMessage>(),
                UpdatedAt: now,
                LastUserMessageAt: null);
        }

        private ChatSessionData? TryLoadLegacy(string sessionId, string characterFingerprint)
        {
            var legacyFilePath = ResolveLegacyFilePath(sessionId);
            if (!File.Exists(legacyFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(legacyFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefault(sessionId, characterFingerprint);
            }

            var legacyFile = JsonUtility.FromJson<LegacyChatSessionFile>(json);
            if (legacyFile == null)
            {
                return CreateDefault(sessionId, characterFingerprint);
            }

            var session = new ChatSessionData(
                SessionId: string.IsNullOrWhiteSpace(legacyFile.sessionId) ? sessionId : legacyFile.sessionId,
                CharacterFingerprint: string.IsNullOrWhiteSpace(legacyFile.characterFingerprint) ? characterFingerprint : legacyFile.characterFingerprint,
                ActiveThreadId: DefaultThreadId,
                Messages: legacyFile.messages != null
                    ? legacyFile.messages.Select(message => message.ToData()).ToArray()
                    : Array.Empty<ChatMessage>(),
                UpdatedAt: DateTimeOffset.UtcNow,
                LastUserMessageAt: null);
            return Normalize(session, sessionId, characterFingerprint);
        }

        private ThreadIndexFile LoadThreadIndex(string sessionId, string characterFingerprint)
        {
            var filePath = ResolveThreadIndexFilePath(characterFingerprint);
            if (!File.Exists(filePath))
            {
                return CreateDefaultThreadIndex(sessionId, characterFingerprint, DefaultThreadId, DateTimeOffset.UtcNow, null);
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultThreadIndex(sessionId, characterFingerprint, DefaultThreadId, DateTimeOffset.UtcNow, null);
            }

            var file = JsonUtility.FromJson<ThreadIndexFile>(json);
            if (file == null)
            {
                return CreateDefaultThreadIndex(sessionId, characterFingerprint, DefaultThreadId, DateTimeOffset.UtcNow, null);
            }

            file.sessionId = string.IsNullOrWhiteSpace(file.sessionId) ? sessionId : file.sessionId.Trim();
            file.characterFingerprint = string.IsNullOrWhiteSpace(file.characterFingerprint) ? characterFingerprint : file.characterFingerprint.Trim();
            file.activeThreadId = string.IsNullOrWhiteSpace(file.activeThreadId) ? DefaultThreadId : file.activeThreadId.Trim();
            file.updatedAtUtc = string.IsNullOrWhiteSpace(file.updatedAtUtc) ? DateTimeOffset.UtcNow.ToString("O") : file.updatedAtUtc;
            file.threads ??= Array.Empty<ThreadIndexEntryFile>();
            return file;
        }

        private CompactIndexFile LoadCompactIndex(string sessionId, string characterFingerprint)
        {
            var filePath = ResolveCompactIndexFilePath(characterFingerprint);
            if (!File.Exists(filePath))
            {
                return CreateDefaultCompactIndex(sessionId, characterFingerprint);
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultCompactIndex(sessionId, characterFingerprint);
            }

            var file = JsonUtility.FromJson<CompactIndexFile>(json);
            if (file == null)
            {
                return CreateDefaultCompactIndex(sessionId, characterFingerprint);
            }

            file.sessionId = string.IsNullOrWhiteSpace(file.sessionId) ? sessionId : file.sessionId.Trim();
            file.characterFingerprint = string.IsNullOrWhiteSpace(file.characterFingerprint) ? characterFingerprint : file.characterFingerprint.Trim();
            file.compacts ??= Array.Empty<CompactIndexEntryFile>();
            return file;
        }

        private void EnsureCompactIndexExists(string sessionId, string characterFingerprint)
        {
            var filePath = ResolveCompactIndexFilePath(characterFingerprint);
            if (File.Exists(filePath))
            {
                return;
            }

            File.WriteAllText(filePath, JsonUtility.ToJson(CreateDefaultCompactIndex(sessionId, characterFingerprint), true));
        }

        private ChatMessage[] LoadThreadMessages(string characterFingerprint, string threadId, string sessionId)
        {
            var filePath = ResolveThreadFilePath(characterFingerprint, threadId);
            if (!File.Exists(filePath))
            {
                return Array.Empty<ChatMessage>();
            }

            return File.ReadAllLines(filePath)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonUtility.FromJson<ChatMessageFile>(line))
                .Where(static file => file != null)
                .Select(file => file!.ToData())
                .Select(message => NormalizeMessage(message, sessionId))
                .OrderBy(message => message.CreatedAt)
                .ToArray();
        }

        private void WriteThreadMessages(
            string characterFingerprint,
            string threadId,
            IReadOnlyList<ChatMessage> messages,
            string sessionId)
        {
            var lines = messages
                .Where(static message => message != null)
                .Select(message => NormalizeMessage(message, sessionId))
                .Select(ChatMessageFile.FromData)
                .Select(file => JsonUtility.ToJson(file))
                .ToArray();
            File.WriteAllLines(ResolveThreadFilePath(characterFingerprint, threadId), lines);
        }

        private static ChatSessionData Normalize(ChatSessionData session, string fallbackSessionId, string fallbackCharacterFingerprint)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(session.SessionId)
                ? fallbackSessionId.Trim()
                : session.SessionId.Trim();
            var normalizedCharacterFingerprint = string.IsNullOrWhiteSpace(session.CharacterFingerprint)
                ? fallbackCharacterFingerprint?.Trim() ?? string.Empty
                : session.CharacterFingerprint.Trim();
            var normalizedActiveThreadId = string.IsNullOrWhiteSpace(session.ActiveThreadId)
                ? DefaultThreadId
                : session.ActiveThreadId.Trim();
            var normalizedMessages = session.Messages
                .Where(static message => message != null)
                .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
                .Select(message => NormalizeMessage(message, normalizedSessionId))
                .OrderBy(message => message.CreatedAt)
                .ToArray();
            var lastUserMessageAt = session.LastUserMessageAt
                ?? normalizedMessages.LastOrDefault(static message => message.Role == ChatRole.User)?.CreatedAt;
            var updatedAt = session.UpdatedAt != default
                ? session.UpdatedAt
                : normalizedMessages.LastOrDefault()?.CreatedAt ?? DateTimeOffset.UtcNow;

            return new ChatSessionData(
                SessionId: normalizedSessionId,
                CharacterFingerprint: normalizedCharacterFingerprint,
                ActiveThreadId: normalizedActiveThreadId,
                Messages: normalizedMessages,
                UpdatedAt: updatedAt,
                LastUserMessageAt: lastUserMessageAt);
        }

        private static ChatCompactData NormalizeCompact(ChatCompactData compact)
        {
            if (string.IsNullOrWhiteSpace(compact.SessionId))
            {
                throw new ArgumentException("A session id is required.", nameof(compact));
            }

            if (string.IsNullOrWhiteSpace(compact.ThreadId))
            {
                throw new ArgumentException("A thread id is required.", nameof(compact));
            }

            if (string.IsNullOrWhiteSpace(compact.CompactId))
            {
                throw new ArgumentException("A compact id is required.", nameof(compact));
            }

            if (string.IsNullOrWhiteSpace(compact.Content))
            {
                throw new ArgumentException("A compact content payload is required.", nameof(compact));
            }

            var normalizedStartedAt = compact.StartedAt == default ? DateTimeOffset.UtcNow : compact.StartedAt;
            var normalizedEndedAt = compact.EndedAt == default ? normalizedStartedAt : compact.EndedAt;
            var normalizedMessageCount = Math.Max(1, compact.MessageCount);
            return compact with
            {
                SessionId = compact.SessionId.Trim(),
                CharacterFingerprint = compact.CharacterFingerprint?.Trim() ?? string.Empty,
                ThreadId = compact.ThreadId.Trim(),
                CompactId = compact.CompactId.Trim(),
                StartedAt = normalizedStartedAt,
                EndedAt = normalizedEndedAt,
                MessageCount = normalizedMessageCount,
                Content = compact.Content.Trim(),
            };
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

        private void EnsureMemoryDirectories(string characterFingerprint)
        {
            Directory.CreateDirectory(ResolveThreadsDirectoryPath(characterFingerprint));
            Directory.CreateDirectory(ResolveCompactsDirectoryPath(characterFingerprint));
        }

        private string ResolveCharacterDirectoryPath(string characterFingerprint)
        {
            var safeFingerprint = ToSafeFileName(string.IsNullOrWhiteSpace(characterFingerprint) ? "default-character" : characterFingerprint.Trim());
            return Path.Combine(charactersDirectoryPath, safeFingerprint, MemoryDirectoryName);
        }

        private string ResolveThreadsDirectoryPath(string characterFingerprint)
        {
            return Path.Combine(ResolveCharacterDirectoryPath(characterFingerprint), ThreadsDirectoryName);
        }

        private string ResolveCompactsDirectoryPath(string characterFingerprint)
        {
            return Path.Combine(ResolveCharacterDirectoryPath(characterFingerprint), CompactsDirectoryName);
        }

        private string ResolveThreadIndexFilePath(string characterFingerprint)
        {
            return Path.Combine(ResolveCharacterDirectoryPath(characterFingerprint), ThreadIndexFileName);
        }

        private string ResolveCompactIndexFilePath(string characterFingerprint)
        {
            return Path.Combine(ResolveCharacterDirectoryPath(characterFingerprint), CompactIndexFileName);
        }

        private string ResolveThreadFilePath(string characterFingerprint, string threadId)
        {
            return Path.Combine(ResolveThreadsDirectoryPath(characterFingerprint), $"{ToSafeFileName(threadId)}.jsonl");
        }

        private string ResolveCompactFilePath(string characterFingerprint, string compactId)
        {
            return Path.Combine(ResolveCompactsDirectoryPath(characterFingerprint), $"{ToSafeFileName(compactId)}.md");
        }

        private string ResolveLegacyFilePath(string sessionId)
        {
            return Path.Combine(legacySessionsDirectoryPath, $"{ToSafeFileName(sessionId)}.json");
        }

        private static ThreadIndexFile CreateDefaultThreadIndex(
            string sessionId,
            string characterFingerprint,
            string activeThreadId,
            DateTimeOffset updatedAt,
            DateTimeOffset? lastUserMessageAt)
        {
            return new ThreadIndexFile
            {
                sessionId = sessionId,
                characterFingerprint = characterFingerprint,
                activeThreadId = activeThreadId,
                updatedAtUtc = updatedAt.ToString("O"),
                lastUserMessageAtUtc = lastUserMessageAt?.ToString("O") ?? string.Empty,
                threads = Array.Empty<ThreadIndexEntryFile>(),
            };
        }

        private static CompactIndexFile CreateDefaultCompactIndex(string sessionId, string characterFingerprint)
        {
            return new CompactIndexFile
            {
                sessionId = sessionId,
                characterFingerprint = characterFingerprint,
                compacts = Array.Empty<CompactIndexEntryFile>(),
            };
        }

        private static DateTimeOffset ResolveCreatedAt(ChatSessionData session)
        {
            return session.Messages.FirstOrDefault()?.CreatedAt
                   ?? session.LastUserMessageAt
                   ?? session.UpdatedAt;
        }

        private static DateTimeOffset ParseTimestamp(string? rawValue)
        {
            return DateTimeOffset.TryParse(rawValue, out var parsedValue)
                ? parsedValue
                : DateTimeOffset.UtcNow;
        }

        private static DateTimeOffset? ParseNullableTimestamp(string? rawValue)
        {
            return DateTimeOffset.TryParse(rawValue, out var parsedValue)
                ? parsedValue
                : null;
        }

        private static string ToSafeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var normalizedCharacters = value
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray();
            var normalized = new string(normalizedCharacters).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
        }

        [Serializable]
        private sealed class LegacyChatSessionFile
        {
            public string sessionId = string.Empty;
            public string characterFingerprint = string.Empty;
            public ChatMessageFile[] messages = Array.Empty<ChatMessageFile>();
        }

        [Serializable]
        private sealed class ThreadIndexFile
        {
            public string sessionId = string.Empty;
            public string characterFingerprint = string.Empty;
            public string activeThreadId = string.Empty;
            public string updatedAtUtc = string.Empty;
            public string lastUserMessageAtUtc = string.Empty;
            public ThreadIndexEntryFile[] threads = Array.Empty<ThreadIndexEntryFile>();
        }

        [Serializable]
        private sealed class ThreadIndexEntryFile
        {
            public string threadId = string.Empty;
            public string createdAtUtc = string.Empty;
            public string updatedAtUtc = string.Empty;
            public int messageCount;
        }

        [Serializable]
        private sealed class CompactIndexFile
        {
            public string sessionId = string.Empty;
            public string characterFingerprint = string.Empty;
            public CompactIndexEntryFile[] compacts = Array.Empty<CompactIndexEntryFile>();
        }

        [Serializable]
        private sealed class CompactIndexEntryFile
        {
            public string compactId = string.Empty;
            public string threadId = string.Empty;
            public string startedAtUtc = string.Empty;
            public string endedAtUtc = string.Empty;
            public int messageCount;
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
        string ActiveThreadId,
        IReadOnlyList<ChatMessage> Messages,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? LastUserMessageAt);

    public sealed record ChatCompactData(
        string SessionId,
        string CharacterFingerprint,
        string ThreadId,
        string CompactId,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        int MessageCount,
        string Content);
}
