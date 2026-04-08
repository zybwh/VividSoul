#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VividSoul.Runtime.AI
{
    public sealed class SoulProfileStore
    {
        private const string LocalAgentDirectoryName = "ai/local-agent";
        private const string UserDirectoryName = "user";
        private const string CharactersDirectoryName = "characters";
        private const string HabitsFileName = "habits.md";
        private const string HabitCandidatesFileName = "habit-candidates.json";
        private const string UserFactsFileName = "user-facts.md";
        private const string RoleFileName = "role.md";
        private const string BondFileName = "bond.md";
        private const string CharacterFactsFileName = "facts.md";
        private const int HabitCandidatePromotionCount = 2;
        private const double HabitCandidatePromotionAverageConfidence = 0.68d;

        private readonly string rootDirectoryPath;
        private readonly object writeGate = new();

        public SoulProfileStore(string? baseDirectory = null)
        {
            var basePath = string.IsNullOrWhiteSpace(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;
            rootDirectoryPath = Path.Combine(basePath, LocalAgentDirectoryName);
        }

        public string LoadHabitsMarkdown()
        {
            var filePath = Path.Combine(rootDirectoryPath, UserDirectoryName, HabitsFileName);
            EnsureFileExists(filePath, BuildHabitsTemplate());
            return NormalizeMarkdown(File.ReadAllText(filePath));
        }

        public string LoadUserFactsMarkdown()
        {
            var filePath = Path.Combine(rootDirectoryPath, UserDirectoryName, UserFactsFileName);
            EnsureFileExists(filePath, BuildUserFactsTemplate());
            return NormalizeMarkdown(File.ReadAllText(filePath));
        }

        public string LoadRoleMarkdown(string characterFingerprint, string characterDisplayName)
        {
            var filePath = Path.Combine(
                rootDirectoryPath,
                CharactersDirectoryName,
                ToSafeDirectoryName(characterFingerprint),
                RoleFileName);
            EnsureFileExists(filePath, BuildRoleTemplate(characterDisplayName));
            return NormalizeMarkdown(File.ReadAllText(filePath));
        }

        public string LoadBondMarkdown(string characterFingerprint, string characterDisplayName)
        {
            var filePath = Path.Combine(
                rootDirectoryPath,
                CharactersDirectoryName,
                ToSafeDirectoryName(characterFingerprint),
                BondFileName);
            EnsureFileExists(filePath, BuildBondTemplate(characterDisplayName));
            return NormalizeMarkdown(File.ReadAllText(filePath));
        }

        public string LoadCharacterFactsMarkdown(string characterFingerprint, string characterDisplayName)
        {
            var filePath = Path.Combine(
                rootDirectoryPath,
                CharactersDirectoryName,
                ToSafeDirectoryName(characterFingerprint),
                "memory",
                CharacterFactsFileName);
            EnsureFileExists(filePath, BuildCharacterFactsTemplate(characterDisplayName));
            return NormalizeMarkdown(File.ReadAllText(filePath));
        }

        public void ApplyMemoryWrites(
            string characterFingerprint,
            string characterDisplayName,
            IReadOnlyList<MemoryWriteEntry> writes)
        {
            if (writes == null || writes.Count == 0)
            {
                return;
            }

            lock (writeGate)
            {
                foreach (var write in writes)
                {
                    ApplyMemoryWrite(characterFingerprint, characterDisplayName, write);
                }
            }
        }

        private void ApplyMemoryWrite(string characterFingerprint, string characterDisplayName, MemoryWriteEntry write)
        {
            if (write == null || string.IsNullOrWhiteSpace(write.Text))
            {
                return;
            }

            var normalizedText = NormalizeBulletText(write.Text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return;
            }

            var managedFiles = BuildManagedMemoryFiles(characterFingerprint, characterDisplayName);
            var target = ResolveWriteTarget(write, managedFiles, characterFingerprint, characterDisplayName);
            if (target == null)
            {
                if (string.Equals(write.MemoryType, "inferredHabitCandidate", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(write.Scope, "user", StringComparison.OrdinalIgnoreCase))
                {
                    RecordHabitCandidate(normalizedText, write.Confidence, write.SourceThreadId);
                }

                return;
            }

            RemoveMatchingBulletFromFiles(managedFiles, write.Replaces);
            RemoveMatchingBulletFromFiles(managedFiles, normalizedText);
            RemoveMatchingHabitCandidate(write.Replaces);
            RemoveMatchingHabitCandidate(normalizedText);
            UpsertBullet(target.FilePath, target.SectionHeading, normalizedText, target.InitialContent);
        }

        private static void EnsureFileExists(string filePath, string initialContent)
        {
            if (File.Exists(filePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("A valid soul profile directory is required.");
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, initialContent);
        }

        private static string BuildRoleTemplate(string characterDisplayName)
        {
            var displayName = ResolveDisplayName(characterDisplayName);
            var updatedAt = DateTimeOffset.Now.ToString("O");
            return string.Join(
                "\n",
                new[]
                {
                    "---",
                    "schema: vivid-soul-role-v1",
                    $"displayName: \"{EscapeQuoted(displayName)}\"",
                    "defaultMood: \"gentle\"",
                    "proactivity: \"low\"",
                    $"updatedAt: \"{updatedAt}\"",
                    "---",
                    string.Empty,
                    "## Identity",
                    string.Empty,
                    $"- `{displayName}` 是当前桌面模型对应的角色身份。",
                    "- 回复时应默认以这个角色本人自称，而不是退回通用 AI 助手口吻。",
                    string.Empty,
                    "## Speech Style",
                    string.Empty,
                    "- 中文为主。",
                    "- 偏口语、自然、简短。",
                    string.Empty,
                    "## Boundaries",
                    string.Empty,
                    "- 不要频繁自报模型或 provider 身份。",
                    "- 除非用户明确追问，否则不要脱离角色讲元信息。",
                    string.Empty,
                    "## Embodiment",
                    string.Empty,
                    "- 回答时默认把当前桌面上的模型当成正在说话的角色本体。",
                    "- 如果用户问“你是谁”，要以角色身份回答。",
                    string.Empty,
                });
        }

        private static string BuildHabitsTemplate()
        {
            var updatedAt = DateTimeOffset.Now.ToString("O");
            return string.Join(
                "\n",
                new[]
                {
                    "---",
                    "schema: vivid-soul-habits-v1",
                    $"updatedAt: \"{updatedAt}\"",
                    "---",
                    string.Empty,
                    "## Stable Preferences",
                    string.Empty,
                    "- 暂无稳定用户习惯记录。",
                    string.Empty,
                    "## Interaction Notes",
                    string.Empty,
                    "- 这里只记录跨会话稳定成立的用户互动习惯。",
                    "- 临时情绪或一次性要求不应长期保留在这里。",
                    string.Empty,
                });
        }

        private static string BuildUserFactsTemplate()
        {
            var updatedAt = DateTimeOffset.Now.ToString("O");
            return string.Join(
                "\n",
                new[]
                {
                    "---",
                    "schema: vivid-soul-user-facts-v1",
                    $"updatedAt: \"{updatedAt}\"",
                    "---",
                    string.Empty,
                    "## Stable Facts",
                    string.Empty,
                    "- 暂无稳定用户事实记录。",
                    string.Empty,
                });
        }

        private static string BuildBondTemplate(string characterDisplayName)
        {
            var displayName = ResolveDisplayName(characterDisplayName);
            var updatedAt = DateTimeOffset.Now.ToString("O");
            return string.Join(
                "\n",
                new[]
                {
                    "---",
                    "schema: vivid-soul-bond-v1",
                    $"displayName: \"{EscapeQuoted(displayName)}\"",
                    $"updatedAt: \"{updatedAt}\"",
                    "---",
                    string.Empty,
                    "## Relationship",
                    string.Empty,
                    $"- `{displayName}` 与当前用户的关系仍在建立阶段。",
                    string.Empty,
                    "## Shared Topics",
                    string.Empty,
                    "- 暂无稳定共同话题。",
                    string.Empty,
                    "## Open Threads",
                    string.Empty,
                    "- 暂无待延续的长期话题。",
                    string.Empty,
                });
        }

        private static string BuildCharacterFactsTemplate(string characterDisplayName)
        {
            var displayName = ResolveDisplayName(characterDisplayName);
            var updatedAt = DateTimeOffset.Now.ToString("O");
            return string.Join(
                "\n",
                new[]
                {
                    "---",
                    "schema: vivid-soul-character-facts-v1",
                    $"displayName: \"{EscapeQuoted(displayName)}\"",
                    $"updatedAt: \"{updatedAt}\"",
                    "---",
                    string.Empty,
                    "## Stable Facts",
                    string.Empty,
                    $"- 暂无 `{displayName}` 相关长期事实记录。",
                    string.Empty,
                });
        }

        private static string ResolveDisplayName(string characterDisplayName)
        {
            return string.IsNullOrWhiteSpace(characterDisplayName)
                ? "VividSoul Mate"
                : characterDisplayName.Trim();
        }

        private static string NormalizeMarkdown(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? string.Empty
                : rawValue.Trim();
        }

        private static string NormalizeBulletText(string rawValue)
        {
            return string.IsNullOrWhiteSpace(rawValue)
                ? string.Empty
                : rawValue
                    .Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal)
                    .Trim();
        }

        private static string EscapeQuoted(string value)
        {
            return value.Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string ToSafeDirectoryName(string characterFingerprint)
        {
            var normalized = string.IsNullOrWhiteSpace(characterFingerprint)
                ? "default-character"
                : characterFingerprint.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(normalized) ? "default-character" : normalized;
        }

        private MemoryWriteTarget? ResolveWriteTarget(
            MemoryWriteEntry write,
            IReadOnlyList<ManagedMemoryFile> managedFiles,
            string characterFingerprint,
            string characterDisplayName)
        {
            if (string.Equals(write.MemoryType, "explicitOverride", StringComparison.OrdinalIgnoreCase))
            {
                var existingTarget = FindMatchingBulletTarget(managedFiles, write.Replaces);
                if (existingTarget != null)
                {
                    return existingTarget;
                }
            }

            return write.MemoryType switch
            {
                "explicitOverride" or "explicitPreference" when string.Equals(write.Scope, "user", StringComparison.OrdinalIgnoreCase)
                    => new MemoryWriteTarget(
                        Path.Combine(rootDirectoryPath, UserDirectoryName, HabitsFileName),
                        "## Stable Preferences",
                        BuildHabitsTemplate()),
                "explicitOverride" or "stableFact" when string.Equals(write.Scope, "user", StringComparison.OrdinalIgnoreCase)
                    => new MemoryWriteTarget(
                        Path.Combine(rootDirectoryPath, UserDirectoryName, UserFactsFileName),
                        "## Stable Facts",
                        BuildUserFactsTemplate()),
                "explicitOverride" or "stableFact" when string.Equals(write.Scope, "character", StringComparison.OrdinalIgnoreCase)
                    => new MemoryWriteTarget(
                        Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), "memory", CharacterFactsFileName),
                        "## Stable Facts",
                        BuildCharacterFactsTemplate(characterDisplayName)),
                "explicitOverride" or "bondUpdate" when string.Equals(write.Scope, "bond", StringComparison.OrdinalIgnoreCase)
                    => new MemoryWriteTarget(
                        Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), BondFileName),
                        "## Relationship",
                        BuildBondTemplate(characterDisplayName)),
                "openCommitment" when string.Equals(write.Scope, "bond", StringComparison.OrdinalIgnoreCase)
                    => new MemoryWriteTarget(
                        Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), BondFileName),
                        "## Open Threads",
                        BuildBondTemplate(characterDisplayName)),
                _ => null,
            };
        }

        private void RecordHabitCandidate(string bulletText, double confidence, string sourceThreadId)
        {
            var filePath = Path.Combine(rootDirectoryPath, UserDirectoryName, HabitCandidatesFileName);
            EnsureFileExists(filePath, BuildHabitCandidatesInitialContent());

            var current = LoadHabitCandidatePool(filePath);
            var comparisonKey = BuildComparisonValue(bulletText);
            if (string.IsNullOrWhiteSpace(comparisonKey))
            {
                return;
            }

            var matchedCandidate = current.Items
                .FirstOrDefault(item => string.Equals(item.ComparisonKey, comparisonKey, StringComparison.Ordinal));
            if (matchedCandidate == null)
            {
                matchedCandidate = new HabitCandidateItem();
                current.Items.Add(matchedCandidate);
            }

            matchedCandidate.Text = bulletText;
            matchedCandidate.ComparisonKey = comparisonKey;
            matchedCandidate.Occurrences = Math.Max(0, matchedCandidate.Occurrences) + 1;
            matchedCandidate.ConfidenceSum += (float)Math.Max(0d, confidence);
            matchedCandidate.MaxConfidence = Math.Max(matchedCandidate.MaxConfidence, (float)confidence);
            matchedCandidate.LastSourceThreadId = string.IsNullOrWhiteSpace(sourceThreadId) ? matchedCandidate.LastSourceThreadId : sourceThreadId;
            matchedCandidate.FirstObservedAtUtc = string.IsNullOrWhiteSpace(matchedCandidate.FirstObservedAtUtc)
                ? DateTimeOffset.UtcNow.ToString("O")
                : matchedCandidate.FirstObservedAtUtc;
            matchedCandidate.LastObservedAtUtc = DateTimeOffset.UtcNow.ToString("O");

            if (ShouldPromoteHabitCandidate(matchedCandidate))
            {
                current.Items.Remove(matchedCandidate);
                UpsertBullet(
                    Path.Combine(rootDirectoryPath, UserDirectoryName, HabitsFileName),
                    "## Stable Preferences",
                    matchedCandidate.Text,
                    BuildHabitsTemplate());
            }

            SaveHabitCandidatePool(filePath, current);
        }

        private void RemoveMatchingHabitCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var filePath = Path.Combine(rootDirectoryPath, UserDirectoryName, HabitCandidatesFileName);
            EnsureFileExists(filePath, BuildHabitCandidatesInitialContent());
            var current = LoadHabitCandidatePool(filePath);
            var comparisonKey = BuildComparisonValue(candidate);
            current.Items = current.Items
                .Where(item => !string.Equals(item.ComparisonKey, comparisonKey, StringComparison.Ordinal))
                .ToList();
            SaveHabitCandidatePool(filePath, current);
        }

        private static bool ShouldPromoteHabitCandidate(HabitCandidateItem candidate)
        {
            if (candidate == null || candidate.Occurrences < HabitCandidatePromotionCount)
            {
                return false;
            }

            var averageConfidence = candidate.Occurrences <= 0
                ? 0d
                : candidate.ConfidenceSum / candidate.Occurrences;
            return averageConfidence >= HabitCandidatePromotionAverageConfidence;
        }

        private static HabitCandidatePoolFile LoadHabitCandidatePool(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var file = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<HabitCandidatePoolFile>(json);
            if (file == null)
            {
                return new HabitCandidatePoolFile();
            }

            file.Items ??= new List<HabitCandidateItem>();
            return file;
        }

        private static void SaveHabitCandidatePool(string filePath, HabitCandidatePoolFile file)
        {
            file.Items ??= new List<HabitCandidateItem>();
            file.Items = file.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Text))
                .OrderByDescending(item => item.Occurrences)
                .ThenByDescending(item => item.MaxConfidence)
                .Take(32)
                .ToList();
            File.WriteAllText(filePath, JsonUtility.ToJson(file, true));
        }

        private static string BuildHabitCandidatesInitialContent()
        {
            return JsonUtility.ToJson(new HabitCandidatePoolFile(), true);
        }

        private IReadOnlyList<ManagedMemoryFile> BuildManagedMemoryFiles(string characterFingerprint, string characterDisplayName)
        {
            var safeCharacterFingerprint = ToSafeDirectoryName(characterFingerprint);
            return new[]
            {
                new ManagedMemoryFile(
                    Path.Combine(rootDirectoryPath, UserDirectoryName, HabitsFileName),
                    BuildHabitsTemplate()),
                new ManagedMemoryFile(
                    Path.Combine(rootDirectoryPath, UserDirectoryName, UserFactsFileName),
                    BuildUserFactsTemplate()),
                new ManagedMemoryFile(
                    Path.Combine(rootDirectoryPath, CharactersDirectoryName, safeCharacterFingerprint, BondFileName),
                    BuildBondTemplate(characterDisplayName)),
                new ManagedMemoryFile(
                    Path.Combine(rootDirectoryPath, CharactersDirectoryName, safeCharacterFingerprint, "memory", CharacterFactsFileName),
                    BuildCharacterFactsTemplate(characterDisplayName)),
            };
        }

        private static MemoryWriteTarget? FindMatchingBulletTarget(IReadOnlyList<ManagedMemoryFile> managedFiles, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            foreach (var managedFile in managedFiles)
            {
                EnsureFileExists(managedFile.FilePath, managedFile.InitialContent);
                var content = File.ReadAllText(managedFile.FilePath);
                var bulletEntry = FindBestMatchingBulletEntry(content, candidate);
                if (bulletEntry != null && !string.IsNullOrWhiteSpace(bulletEntry.SectionHeading))
                {
                    return new MemoryWriteTarget(managedFile.FilePath, bulletEntry.SectionHeading, managedFile.InitialContent);
                }
            }

            return null;
        }

        private static void RemoveMatchingBulletFromFiles(IReadOnlyList<ManagedMemoryFile> managedFiles, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            foreach (var managedFile in managedFiles)
            {
                EnsureFileExists(managedFile.FilePath, managedFile.InitialContent);
                var current = File.ReadAllText(managedFile.FilePath);
                var updated = RemoveMatchingBullet(current, candidate);
                if (!string.Equals(current, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(managedFile.FilePath, TouchUpdatedAt(updated));
                }
            }
        }

        private static void UpsertBullet(string filePath, string sectionHeading, string bulletText, string initialContent)
        {
            EnsureFileExists(filePath, initialContent);
            var current = File.ReadAllText(filePath);
            var updated = RemoveMatchingBullet(current, bulletText);
            updated = InsertBullet(updated, sectionHeading, bulletText);
            File.WriteAllText(filePath, TouchUpdatedAt(updated));
        }

        private static string RemoveMatchingBullet(string content, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var matchedBullet = FindBestMatchingBulletEntry(content, candidate);
            return matchedBullet == null
                ? content
                : RemoveExactBullet(content, matchedBullet.Text);
        }

        private static string RemoveExactBullet(string content, string bulletText)
        {
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var filtered = lines
                .Where(line => !string.Equals(line.Trim(), $"- {bulletText.Trim()}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return string.Join("\n", filtered);
        }

        private static string InsertBullet(string content, string sectionHeading, string bulletText)
        {
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            var headingIndex = normalized.IndexOf(sectionHeading, StringComparison.Ordinal);
            if (headingIndex < 0)
            {
                return $"{normalized.TrimEnd()}\n\n{sectionHeading}\n\n- {bulletText}\n";
            }

            var insertSearchStart = headingIndex + sectionHeading.Length;
            var nextSectionIndex = normalized.IndexOf("\n## ", insertSearchStart, StringComparison.Ordinal);
            var insertionIndex = nextSectionIndex >= 0 ? nextSectionIndex : normalized.Length;
            var sectionBody = normalized.Substring(insertSearchStart, insertionIndex - insertSearchStart);
            var cleanedSectionBody = string.Join(
                "\n",
                sectionBody
                    .Split('\n')
                    .Where(line => !IsPlaceholderBullet(line)))
                .TrimEnd();
            var prefix = normalized[..insertSearchStart];
            var suffix = normalized[insertionIndex..];
            var joinedBody = string.IsNullOrWhiteSpace(cleanedSectionBody)
                ? $"\n\n- {bulletText}\n"
                : $"{cleanedSectionBody}\n- {bulletText}\n";
            return $"{prefix}{joinedBody}{suffix}".TrimEnd() + "\n";
        }

        private static BulletEntry? FindBestMatchingBulletEntry(string content, string candidate)
        {
            var comparisonCandidate = BuildComparisonValue(candidate);
            if (string.IsNullOrWhiteSpace(comparisonCandidate))
            {
                return null;
            }

            return EnumerateBulletEntries(content)
                .Select(entry => new
                {
                    Entry = entry,
                    Score = GetMatchScore(entry.Text, comparisonCandidate),
                })
                .Where(result => result.Score > 0)
                .OrderByDescending(result => result.Score)
                .Select(result => result.Entry)
                .FirstOrDefault();
        }

        private static IEnumerable<BulletEntry> EnumerateBulletEntries(string content)
        {
            var currentSectionHeading = string.Empty;
            foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("## ", StringComparison.Ordinal))
                {
                    currentSectionHeading = trimmedLine;
                    continue;
                }

                if (trimmedLine.StartsWith("- ", StringComparison.Ordinal))
                {
                    var bulletText = NormalizeBulletText(trimmedLine[2..]);
                    if (!string.IsNullOrWhiteSpace(bulletText))
                    {
                        yield return new BulletEntry(currentSectionHeading, bulletText);
                    }
                }
            }
        }

        private static int GetMatchScore(string existingText, string comparisonCandidate)
        {
            var existingComparisonValue = BuildComparisonValue(existingText);
            if (string.IsNullOrWhiteSpace(existingComparisonValue))
            {
                return 0;
            }

            if (string.Equals(existingComparisonValue, comparisonCandidate, StringComparison.Ordinal))
            {
                return 1000 + existingComparisonValue.Length;
            }

            if (existingComparisonValue.Contains(comparisonCandidate, StringComparison.Ordinal)
                || comparisonCandidate.Contains(existingComparisonValue, StringComparison.Ordinal))
            {
                return 500 + Math.Min(existingComparisonValue.Length, comparisonCandidate.Length);
            }

            return 0;
        }

        private static bool IsPlaceholderBullet(string line)
        {
            var trimmedLine = line.Trim();
            return trimmedLine.StartsWith("- 暂无", StringComparison.Ordinal);
        }

        private static string TouchUpdatedAt(string content)
        {
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
            {
                return content;
            }

            for (var index = 1; index < lines.Length; index++)
            {
                if (string.Equals(lines[index].Trim(), "---", StringComparison.Ordinal))
                {
                    break;
                }

                if (lines[index].TrimStart().StartsWith("updatedAt:", StringComparison.Ordinal))
                {
                    lines[index] = $"updatedAt: \"{DateTimeOffset.Now:O}\"";
                    return string.Join("\n", lines).TrimEnd() + "\n";
                }
            }

            return content;
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

        private sealed record ManagedMemoryFile(string FilePath, string InitialContent);

        private sealed record MemoryWriteTarget(string FilePath, string SectionHeading, string InitialContent);

        private sealed record BulletEntry(string SectionHeading, string Text);

        [Serializable]
        private sealed class HabitCandidatePoolFile
        {
            public List<HabitCandidateItem> Items = new();
        }

        [Serializable]
        private sealed class HabitCandidateItem
        {
            public string Text = string.Empty;
            public string ComparisonKey = string.Empty;
            public int Occurrences;
            public float ConfidenceSum;
            public float MaxConfidence;
            public string FirstObservedAtUtc = string.Empty;
            public string LastObservedAtUtc = string.Empty;
            public string LastSourceThreadId = string.Empty;
        }
    }
}
