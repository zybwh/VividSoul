#nullable enable

using System;
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
        private const string UserFactsFileName = "user-facts.md";
        private const string RoleFileName = "role.md";
        private const string BondFileName = "bond.md";
        private const string CharacterFactsFileName = "facts.md";

        private readonly string rootDirectoryPath;

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
            System.Collections.Generic.IReadOnlyList<MemoryWriteEntry> writes)
        {
            if (writes == null || writes.Count == 0)
            {
                return;
            }

            foreach (var write in writes)
            {
                ApplyMemoryWrite(characterFingerprint, characterDisplayName, write);
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

            switch (write.MemoryType)
            {
                case "explicitOverride":
                case "explicitPreference":
                    if (string.Equals(write.Scope, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        UpsertBullet(Path.Combine(rootDirectoryPath, UserDirectoryName, HabitsFileName), "## Stable Preferences", normalizedText, write.Replaces, BuildHabitsTemplate());
                    }

                    break;
                case "stableFact":
                    if (string.Equals(write.Scope, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        UpsertBullet(Path.Combine(rootDirectoryPath, UserDirectoryName, UserFactsFileName), "## Stable Facts", normalizedText, write.Replaces, BuildUserFactsTemplate());
                    }
                    else if (string.Equals(write.Scope, "character", StringComparison.OrdinalIgnoreCase))
                    {
                        UpsertBullet(
                            Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), "memory", CharacterFactsFileName),
                            "## Stable Facts",
                            normalizedText,
                            write.Replaces,
                            BuildCharacterFactsTemplate(characterDisplayName));
                    }

                    break;
                case "bondUpdate":
                    UpsertBullet(
                        Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), BondFileName),
                        "## Relationship",
                        normalizedText,
                        write.Replaces,
                        BuildBondTemplate(characterDisplayName));
                    break;
                case "openCommitment":
                    UpsertBullet(
                        Path.Combine(rootDirectoryPath, CharactersDirectoryName, ToSafeDirectoryName(characterFingerprint), BondFileName),
                        "## Open Threads",
                        normalizedText,
                        write.Replaces,
                        BuildBondTemplate(characterDisplayName));
                    break;
            }
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

        private static void UpsertBullet(string filePath, string sectionHeading, string bulletText, string? replaces, string initialContent)
        {
            EnsureFileExists(filePath, initialContent);
            var current = File.ReadAllText(filePath);
            var updated = RemoveBullet(current, replaces);
            updated = RemoveBullet(updated, bulletText);
            updated = InsertBullet(updated, sectionHeading, bulletText);
            File.WriteAllText(filePath, updated);
        }

        private static string RemoveBullet(string content, string? bulletText)
        {
            if (string.IsNullOrWhiteSpace(bulletText) || string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

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
            var cleanedSectionBody = sectionBody.TrimEnd();
            var prefix = normalized[..insertSearchStart];
            var suffix = normalized[insertionIndex..];
            var joinedBody = string.IsNullOrWhiteSpace(cleanedSectionBody)
                ? $"\n\n- {bulletText}\n"
                : $"{cleanedSectionBody}\n- {bulletText}\n";
            return $"{prefix}{joinedBody}{suffix}".TrimEnd() + "\n";
        }
    }
}
