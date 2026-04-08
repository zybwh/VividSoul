#nullable enable

using System;
using System.Collections.Generic;

namespace VividSoul.Runtime.AI
{
    public sealed class SoulPromptAssembler
    {
        private const string ResponseFormatInstruction = "Reply as natural spoken dialogue instead of markdown or document style. Do not use headings, bullet lists, numbered lists, code fences, markdown emphasis, XML-style tags, or roleplay markers. Keep the reply in one short paragraph with no unnecessary line breaks, and prefer concise colloquial Chinese when the user is speaking Chinese.";
        private readonly SoulProfileStore soulProfileStore;

        public SoulPromptAssembler(SoulProfileStore soulProfileStore)
        {
            this.soulProfileStore = soulProfileStore ?? throw new ArgumentNullException(nameof(soulProfileStore));
        }

        public string BuildSystemPrompt(
            string globalSystemPrompt,
            string characterDisplayName,
            string characterFingerprint)
        {
            var sections = new List<string>();
            var normalizedGlobalSystemPrompt = globalSystemPrompt?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedGlobalSystemPrompt))
            {
                sections.Add(normalizedGlobalSystemPrompt);
            }

            var normalizedDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? "the current VividSoul desktop mate"
                : characterDisplayName.Trim();
            sections.Add($"Current desktop mate character: {normalizedDisplayName}.");
            sections.Add("Treat the currently rendered desktop model as the embodied speaker. Stay in character by default, and answer identity questions from the role's point of view unless the user explicitly asks for out-of-character meta information.");
            sections.Add("Use the following local soul context as high-priority grounding for identity, user habits, and the current relationship state.");
            sections.Add(BuildMarkdownSection("ROLE", soulProfileStore.LoadRoleMarkdown(characterFingerprint, characterDisplayName)));
            sections.Add(BuildMarkdownSection("USER_HABITS", soulProfileStore.LoadHabitsMarkdown()));
            sections.Add(BuildMarkdownSection("USER_FACTS", soulProfileStore.LoadUserFactsMarkdown()));
            sections.Add(BuildMarkdownSection("BOND", soulProfileStore.LoadBondMarkdown(characterFingerprint, characterDisplayName)));
            sections.Add(BuildMarkdownSection("CHARACTER_FACTS", soulProfileStore.LoadCharacterFactsMarkdown(characterFingerprint, characterDisplayName)));
            sections.Add(ResponseFormatInstruction);
            return string.Join("\n\n", sections);
        }

        private static string BuildMarkdownSection(string title, string content)
        {
            return string.IsNullOrWhiteSpace(content)
                ? $"[{title}]\n<empty>"
                : $"[{title}]\n{content.Trim()}";
        }
    }
}
