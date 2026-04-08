#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public sealed class ReminderStore
    {
        private const string RemindersDirectoryName = "ai/local-agent/reminders";
        private const string ItemsDirectoryName = "items";
        private const string IndexFileName = "index.json";
        private readonly string remindersDirectoryPath;
        private readonly object gate = new();

        public ReminderStore(string? baseDirectory = null)
        {
            var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            remindersDirectoryPath = Path.Combine(rootDirectory, RemindersDirectoryName);
        }

        public ReminderRecord CreateOrGetExisting(ReminderRecord reminder)
        {
            if (reminder == null)
            {
                throw new ArgumentNullException(nameof(reminder));
            }

            lock (gate)
            {
                EnsureStorageExists();
                var index = LoadIndex();
                var existing = index.Items
                    .Select(entry => LoadReminder(entry.Id))
                    .FirstOrDefault(candidate => candidate != null && IsEquivalentPendingReminder(candidate, reminder));
                if (existing != null)
                {
                    return existing;
                }

                SaveReminderInternal(reminder);
                UpsertIndexEntry(index, reminder);
                SaveIndex(index);
                return reminder;
            }
        }

        public IReadOnlyList<ReminderRecord> LoadDuePending(DateTimeOffset nowUtc)
        {
            lock (gate)
            {
                EnsureStorageExists();
                return LoadIndex().Items
                    .Where(entry => string.Equals(entry.Status, ReminderStatus.Pending.ToStorageValue(), StringComparison.Ordinal))
                    .Select(entry => LoadReminder(entry.Id))
                    .Where(reminder => reminder != null && reminder.Status == ReminderStatus.Pending && reminder.DueAtUtc <= nowUtc)
                    .Cast<ReminderRecord>()
                    .OrderBy(reminder => reminder.DueAtUtc)
                    .ToArray();
            }
        }

        public string BuildPendingReminderPromptContext(string characterFingerprint, int maxCount = 6)
        {
            lock (gate)
            {
                EnsureStorageExists();
                var normalizedCharacterFingerprint = characterFingerprint?.Trim() ?? string.Empty;
                var reminders = LoadIndex().Items
                    .Where(entry => string.Equals(entry.Status, ReminderStatus.Pending.ToStorageValue(), StringComparison.Ordinal))
                    .Select(entry => LoadReminder(entry.Id))
                    .Where(static reminder => reminder != null && reminder.Status == ReminderStatus.Pending)
                    .Cast<ReminderRecord>()
                    .OrderByDescending(reminder => string.Equals(reminder.CharacterFingerprint, normalizedCharacterFingerprint, StringComparison.Ordinal))
                    .ThenBy(reminder => reminder.DueAtUtc)
                    .Take(Math.Max(1, maxCount))
                    .ToArray();
                if (reminders.Length == 0)
                {
                    return "<empty>";
                }

                return string.Join(
                    "\n",
                    reminders.Select(reminder =>
                    {
                        var ownership = string.Equals(reminder.CharacterFingerprint, normalizedCharacterFingerprint, StringComparison.Ordinal)
                            ? "current-character"
                            : "other-character";
                        return $"- [{ownership}] {reminder.Title} | dueAtUtc={reminder.DueAtUtc:O} | note={reminder.Note}";
                    }));
            }
        }

        public IReadOnlyList<ReminderRecord> LoadAll()
        {
            lock (gate)
            {
                EnsureStorageExists();
                return LoadIndex().Items
                    .Select(entry => LoadReminder(entry.Id))
                    .Where(static reminder => reminder != null)
                    .Cast<ReminderRecord>()
                    .OrderBy(reminder => reminder.DueAtUtc)
                    .ToArray();
            }
        }

        public ReminderRecord? TryCancelPending(string title, string sourceText, string characterFingerprint)
        {
            lock (gate)
            {
                return TryUpdatePendingReminder(title, sourceText, characterFingerprint, ReminderStatus.Cancelled);
            }
        }

        public ReminderRecord? TryCompletePending(string title, string sourceText, string characterFingerprint)
        {
            lock (gate)
            {
                return TryUpdatePendingReminder(title, sourceText, characterFingerprint, ReminderStatus.Completed);
            }
        }

        public ReminderRecord MarkFiring(string reminderId, DateTimeOffset nowUtc)
        {
            lock (gate)
            {
                var reminder = LoadReminder(reminderId) ?? throw new InvalidOperationException("Reminder record was not found.");
                var updated = reminder with
                {
                    Status = ReminderStatus.Firing,
                    UpdatedAtUtc = nowUtc,
                };
                SaveReminderInternal(updated);
                SaveIndexEntry(updated);
                return updated;
            }
        }

        public ReminderRecord MarkDelivered(string reminderId, DateTimeOffset nowUtc)
        {
            lock (gate)
            {
                var reminder = LoadReminder(reminderId) ?? throw new InvalidOperationException("Reminder record was not found.");
                var updated = reminder with
                {
                    Status = ReminderStatus.Delivered,
                    DeliveredAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                };
                SaveReminderInternal(updated);
                SaveIndexEntry(updated);
                return updated;
            }
        }

        private ReminderRecord? TryUpdatePendingReminder(
            string title,
            string sourceText,
            string characterFingerprint,
            ReminderStatus targetStatus)
        {
            EnsureStorageExists();
            var index = LoadIndex();
            var target = index.Items
                .Where(entry => string.Equals(entry.Status, ReminderStatus.Pending.ToStorageValue(), StringComparison.Ordinal))
                .Select(entry => LoadReminder(entry.Id))
                .Where(static reminder => reminder != null && reminder.Status == ReminderStatus.Pending)
                .Cast<ReminderRecord>()
                .OrderByDescending(reminder => reminder.CreatedAtUtc)
                .Select(reminder => new
                {
                    Reminder = reminder,
                    Score = GetMatchScore(reminder, title, sourceText, characterFingerprint),
                })
                .Where(result => result.Score > 0)
                .OrderByDescending(result => result.Score)
                .ThenByDescending(result => result.Reminder.CreatedAtUtc)
                .Select(result => result.Reminder)
                .FirstOrDefault();
            if (target == null)
            {
                return null;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var updated = target with
            {
                Status = targetStatus,
                UpdatedAtUtc = nowUtc,
            };
            SaveReminderInternal(updated);
            SaveIndexEntry(updated);
            return updated;
        }

        private void SaveIndexEntry(ReminderRecord reminder)
        {
            var index = LoadIndex();
            UpsertIndexEntry(index, reminder);
            SaveIndex(index);
        }

        private static int GetMatchScore(ReminderRecord reminder, string title, string sourceText, string characterFingerprint)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(characterFingerprint)
                && string.Equals(reminder.CharacterFingerprint, characterFingerprint.Trim(), StringComparison.Ordinal))
            {
                score += 100;
            }

            var titleKey = BuildComparisonValue(title);
            var reminderTitleKey = BuildComparisonValue(reminder.Title);
            if (!string.IsNullOrWhiteSpace(titleKey) && !string.IsNullOrWhiteSpace(reminderTitleKey))
            {
                if (string.Equals(titleKey, reminderTitleKey, StringComparison.Ordinal))
                {
                    score += 1000;
                }
                else if (titleKey.Contains(reminderTitleKey, StringComparison.Ordinal)
                         || reminderTitleKey.Contains(titleKey, StringComparison.Ordinal))
                {
                    score += 600;
                }
            }

            var sourceKey = BuildComparisonValue(sourceText);
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                if (sourceKey.Contains("刚才", StringComparison.Ordinal)
                    || sourceKey.Contains("刚刚", StringComparison.Ordinal)
                    || sourceKey.Contains("那个", StringComparison.Ordinal)
                    || sourceKey.Contains("上一", StringComparison.Ordinal)
                    || sourceKey.Contains("上个", StringComparison.Ordinal))
                {
                    score += 200;
                }
            }

            return score;
        }

        private static bool IsEquivalentPendingReminder(ReminderRecord left, ReminderRecord right)
        {
            return left.Status == ReminderStatus.Pending
                && right.Status == ReminderStatus.Pending
                && string.Equals(BuildComparisonValue(left.Title), BuildComparisonValue(right.Title), StringComparison.Ordinal)
                && string.Equals(left.CharacterFingerprint, right.CharacterFingerprint, StringComparison.Ordinal)
                && Math.Abs((left.DueAtUtc - right.DueAtUtc).TotalSeconds) < 60d;
        }

        private void SaveReminderInternal(ReminderRecord reminder)
        {
            File.WriteAllText(ResolveItemFilePath(reminder.Id), JsonUtility.ToJson(ReminderRecordFile.FromData(reminder), true));
        }

        private ReminderRecord? LoadReminder(string reminderId)
        {
            var filePath = ResolveItemFilePath(reminderId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var file = JsonUtility.FromJson<ReminderRecordFile>(json);
            return file?.ToData();
        }

        private ReminderIndexFile LoadIndex()
        {
            var filePath = ResolveIndexFilePath();
            if (!File.Exists(filePath))
            {
                return new ReminderIndexFile();
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ReminderIndexFile();
            }

            var file = JsonUtility.FromJson<ReminderIndexFile>(json);
            if (file == null)
            {
                return new ReminderIndexFile();
            }

            file.Items ??= Array.Empty<ReminderIndexEntryFile>();
            return file;
        }

        private void SaveIndex(ReminderIndexFile index)
        {
            index.Items ??= Array.Empty<ReminderIndexEntryFile>();
            index.Items = index.Items
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Id))
                .OrderBy(entry => entry.DueAtUtc)
                .ToArray();
            File.WriteAllText(ResolveIndexFilePath(), JsonUtility.ToJson(index, true));
        }

        private static void UpsertIndexEntry(ReminderIndexFile index, ReminderRecord reminder)
        {
            index.Items = index.Items
                .Where(entry => entry != null && !string.Equals(entry.Id, reminder.Id, StringComparison.Ordinal))
                .Append(ReminderIndexEntryFile.FromData(reminder))
                .ToArray();
        }

        private void EnsureStorageExists()
        {
            Directory.CreateDirectory(remindersDirectoryPath);
            Directory.CreateDirectory(Path.Combine(remindersDirectoryPath, ItemsDirectoryName));
            var indexFilePath = ResolveIndexFilePath();
            if (!File.Exists(indexFilePath))
            {
                File.WriteAllText(indexFilePath, JsonUtility.ToJson(new ReminderIndexFile(), true));
            }
        }

        private string ResolveIndexFilePath()
        {
            return Path.Combine(remindersDirectoryPath, IndexFileName);
        }

        private string ResolveItemFilePath(string reminderId)
        {
            return Path.Combine(remindersDirectoryPath, ItemsDirectoryName, $"{reminderId}.json");
        }

        private static string BuildComparisonValue(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? string.Empty
                : new string(rawValue
                    .Where(static character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && character != '`')
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        [Serializable]
        private sealed class ReminderIndexFile
        {
            public ReminderIndexEntryFile[] Items = Array.Empty<ReminderIndexEntryFile>();
        }

        [Serializable]
        private sealed class ReminderIndexEntryFile
        {
            public string Id = string.Empty;
            public string Title = string.Empty;
            public string DueAtUtc = string.Empty;
            public string Status = ReminderStatus.Pending.ToStorageValue();
            public string CharacterFingerprint = string.Empty;

            public static ReminderIndexEntryFile FromData(ReminderRecord data)
            {
                return new ReminderIndexEntryFile
                {
                    Id = data.Id,
                    Title = data.Title,
                    DueAtUtc = data.DueAtUtc.ToString("O"),
                    Status = data.Status.ToStorageValue(),
                    CharacterFingerprint = data.CharacterFingerprint,
                };
            }
        }

        [Serializable]
        private sealed class ReminderRecordFile
        {
            public string Id = string.Empty;
            public string Title = string.Empty;
            public string Note = string.Empty;
            public string DueAtUtc = string.Empty;
            public string Timezone = string.Empty;
            public string Status = ReminderStatus.Pending.ToStorageValue();
            public string CreatedAtUtc = string.Empty;
            public string UpdatedAtUtc = string.Empty;
            public string CharacterFingerprint = string.Empty;
            public string SourceThreadId = string.Empty;
            public string DeliveredAtUtc = string.Empty;
            public string AcknowledgedAtUtc = string.Empty;

            public ReminderRecord ToData()
            {
                return new ReminderRecord(
                    Id: Id ?? string.Empty,
                    Title: Title ?? string.Empty,
                    Note: Note ?? string.Empty,
                    DueAtUtc: ParseTimestamp(DueAtUtc),
                    Timezone: Timezone ?? string.Empty,
                    Status: ReminderStatusExtensions.FromStorageValue(Status),
                    CreatedAtUtc: ParseTimestamp(CreatedAtUtc),
                    UpdatedAtUtc: ParseTimestamp(UpdatedAtUtc),
                    CharacterFingerprint: CharacterFingerprint ?? string.Empty,
                    SourceThreadId: SourceThreadId ?? string.Empty,
                    DeliveredAtUtc: ParseNullableTimestamp(DeliveredAtUtc),
                    AcknowledgedAtUtc: ParseNullableTimestamp(AcknowledgedAtUtc));
            }

            public static ReminderRecordFile FromData(ReminderRecord data)
            {
                return new ReminderRecordFile
                {
                    Id = data.Id,
                    Title = data.Title,
                    Note = data.Note,
                    DueAtUtc = data.DueAtUtc.ToString("O"),
                    Timezone = data.Timezone,
                    Status = data.Status.ToStorageValue(),
                    CreatedAtUtc = data.CreatedAtUtc.ToString("O"),
                    UpdatedAtUtc = data.UpdatedAtUtc.ToString("O"),
                    CharacterFingerprint = data.CharacterFingerprint,
                    SourceThreadId = data.SourceThreadId,
                    DeliveredAtUtc = data.DeliveredAtUtc?.ToString("O") ?? string.Empty,
                    AcknowledgedAtUtc = data.AcknowledgedAtUtc?.ToString("O") ?? string.Empty,
                };
            }

            private static DateTimeOffset ParseTimestamp(string rawValue)
            {
                return DateTimeOffset.TryParse(rawValue, out var timestamp)
                    ? timestamp.ToUniversalTime()
                    : DateTimeOffset.UtcNow;
            }

            private static DateTimeOffset? ParseNullableTimestamp(string rawValue)
            {
                return DateTimeOffset.TryParse(rawValue, out var timestamp)
                    ? timestamp.ToUniversalTime()
                    : null;
            }
        }
    }

    public sealed record ReminderRecord(
        string Id,
        string Title,
        string Note,
        DateTimeOffset DueAtUtc,
        string Timezone,
        ReminderStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string CharacterFingerprint,
        string SourceThreadId,
        DateTimeOffset? DeliveredAtUtc,
        DateTimeOffset? AcknowledgedAtUtc);

    public enum ReminderStatus
    {
        Pending = 0,
        Firing = 1,
        Delivered = 2,
        Cancelled = 3,
        Completed = 4,
    }

    public static class ReminderStatusExtensions
    {
        public static string ToStorageValue(this ReminderStatus status)
        {
            return status switch
            {
                ReminderStatus.Pending => "pending",
                ReminderStatus.Firing => "firing",
                ReminderStatus.Delivered => "delivered",
                ReminderStatus.Cancelled => "cancelled",
                ReminderStatus.Completed => "completed",
                _ => "pending",
            };
        }

        public static ReminderStatus FromStorageValue(string rawValue)
        {
            return rawValue?.Trim().ToLowerInvariant() switch
            {
                "pending" => ReminderStatus.Pending,
                "firing" => ReminderStatus.Firing,
                "delivered" => ReminderStatus.Delivered,
                "cancelled" => ReminderStatus.Cancelled,
                "completed" => ReminderStatus.Completed,
                _ => ReminderStatus.Pending,
            };
        }
    }
}
