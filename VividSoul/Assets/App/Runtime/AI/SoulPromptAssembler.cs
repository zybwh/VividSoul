#nullable enable

using System;
using System.Collections.Generic;

namespace VividSoul.Runtime.AI
{
    public sealed class SoulPromptAssembler
    {
        private const int RoleSectionMaxCharacters = 1000;
        private const int HabitsSectionMaxCharacters = 600;
        private const int UserFactsSectionMaxCharacters = 600;
        private const int BondSectionMaxCharacters = 700;
        private const int CharacterFactsSectionMaxCharacters = 700;
        private const int PendingRemindersSectionMaxCharacters = 400;
        private const string ResponseFormatInstruction = "Reply as natural spoken dialogue. Do not use markdown, lists, code fences, XML-style tags, or roleplay markers. Keep the reply short, plain, and colloquial Chinese when the user is speaking Chinese.";
        private readonly SoulProfileStore soulProfileStore;
        private readonly ReminderStore reminderStore;

        public SoulPromptAssembler(SoulProfileStore soulProfileStore, ReminderStore? reminderStore = null)
        {
            this.soulProfileStore = soulProfileStore ?? throw new ArgumentNullException(nameof(soulProfileStore));
            this.reminderStore = reminderStore ?? new ReminderStore();
        }

        public string BuildSystemPrompt(
            string globalSystemPrompt,
            string characterDisplayName,
            string characterFingerprint,
            string supplementalInstruction = "")
        {
            var sections = new List<string>();
            var normalizedGlobalSystemPrompt = globalSystemPrompt?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedGlobalSystemPrompt))
            {
                sections.Add(normalizedGlobalSystemPrompt);
            }

            var normalizedSupplementalInstruction = supplementalInstruction?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedSupplementalInstruction))
            {
                sections.Add($"Turn-specific instruction:\n{normalizedSupplementalInstruction}");
            }

            var normalizedDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? "the current VividSoul desktop mate"
                : characterDisplayName.Trim();
            sections.Add($"Current desktop mate character: {normalizedDisplayName}. Stay in character and treat the currently rendered model as the speaker unless the user explicitly asks for meta information.");
            sections.Add("Use the following local grounding when relevant.");
            sections.Add(BuildMarkdownSection("ROLE", soulProfileStore.LoadRoleMarkdown(characterFingerprint, characterDisplayName), RoleSectionMaxCharacters));
            sections.Add(BuildMarkdownSection("USER_HABITS", soulProfileStore.LoadHabitsMarkdown(), HabitsSectionMaxCharacters));
            sections.Add(BuildMarkdownSection("USER_FACTS", soulProfileStore.LoadUserFactsMarkdown(), UserFactsSectionMaxCharacters));
            sections.Add(BuildMarkdownSection("BOND", soulProfileStore.LoadBondMarkdown(characterFingerprint, characterDisplayName), BondSectionMaxCharacters));
            sections.Add(BuildMarkdownSection("CHARACTER_FACTS", soulProfileStore.LoadCharacterFactsMarkdown(characterFingerprint, characterDisplayName), CharacterFactsSectionMaxCharacters));
            sections.Add(BuildMarkdownSection("PENDING_REMINDERS", reminderStore.BuildPendingReminderPromptContext(characterFingerprint), PendingRemindersSectionMaxCharacters));
            sections.Add(ResponseFormatInstruction);
            return string.Join("\n\n", sections);
        }

        private static string BuildMarkdownSection(string title, string content, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return $"[{title}]\n<empty>";
            }

            var normalizedContent = content.Trim();
            if (normalizedContent.Length > maxCharacters)
            {
                normalizedContent = $"{normalizedContent[..maxCharacters].TrimEnd()}\n...[truncated]";
            }

            return string.IsNullOrWhiteSpace(normalizedContent)
                ? $"[{title}]\n<empty>"
                : $"[{title}]\n{normalizedContent}";
        }
    }
}
