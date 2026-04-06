#nullable enable

using System.Text.RegularExpressions;

namespace VividSoul.Runtime.AI
{
    public static class LlmDialogueTextFormatter
    {
        private static readonly Regex ThinkTagRegex = new(
            "<think>.*?</think>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CodeFenceRegex = new(
            "```+",
            RegexOptions.Compiled);

        private static readonly Regex HeadingPrefixRegex = new(
            @"^\s{0,3}#{1,6}\s*",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex BulletPrefixRegex = new(
            @"^\s*([-*•]+|\d+\.)\s+",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex BoldAsteriskRegex = new(
            @"\*\*(.+?)\*\*",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex BoldUnderlineRegex = new(
            @"__(.+?)__",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ItalicAsteriskRegex = new(
            @"(?<!\*)\*(?!\s)(.+?)(?<!\s)\*(?!\*)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ItalicUnderlineRegex = new(
            @"(?<!_)_(?!\s)(.+?)(?<!\s)_(?!_)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex InlineCodeRegex = new(
            @"`([^`]+)`",
            RegexOptions.Compiled);

        private static readonly Regex HtmlLikeTagRegex = new(
            @"</?[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex MultiNewLineRegex = new(
            @"\n+",
            RegexOptions.Compiled);

        private static readonly Regex MultiWhitespaceRegex = new(
            @"[ \t\u00A0]{2,}",
            RegexOptions.Compiled);

        private static readonly Regex SpaceBeforePunctuationRegex = new(
            @"\s+([，。！？；：、,.!?;:）】》”])",
            RegexOptions.Compiled);

        private static readonly Regex SpaceAfterOpeningRegex = new(
            @"([（【《“])\s+",
            RegexOptions.Compiled);

        public static string ToDisplayRichText(string rawText)
        {
            var normalized = NormalizeBase(rawText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = BoldAsteriskRegex.Replace(normalized, "<b>$1</b>");
            normalized = BoldUnderlineRegex.Replace(normalized, "<b>$1</b>");
            normalized = ItalicAsteriskRegex.Replace(normalized, "<i>$1</i>");
            normalized = ItalicUnderlineRegex.Replace(normalized, "<i>$1</i>");
            normalized = RemoveResidualMarkdown(normalized);
            normalized = CleanupWhitespace(normalized);
            return normalized.Trim();
        }

        public static string ToPlainDialogueText(string rawText)
        {
            var normalized = NormalizeBase(rawText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = BoldAsteriskRegex.Replace(normalized, "$1");
            normalized = BoldUnderlineRegex.Replace(normalized, "$1");
            normalized = ItalicAsteriskRegex.Replace(normalized, "$1");
            normalized = ItalicUnderlineRegex.Replace(normalized, "$1");
            normalized = RemoveResidualMarkdown(normalized);
            normalized = CleanupWhitespace(normalized);
            return normalized.Trim();
        }

        private static string NormalizeBase(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            var normalized = rawText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            normalized = ThinkTagRegex.Replace(normalized, string.Empty);
            normalized = CodeFenceRegex.Replace(normalized, string.Empty);
            normalized = HeadingPrefixRegex.Replace(normalized, string.Empty);
            normalized = BulletPrefixRegex.Replace(normalized, string.Empty);
            normalized = InlineCodeRegex.Replace(normalized, "$1");
            normalized = HtmlLikeTagRegex.Replace(normalized, string.Empty);
            normalized = MultiNewLineRegex.Replace(normalized, " ");
            return normalized;
        }

        private static string RemoveResidualMarkdown(string text)
        {
            return text
                .Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace('*', ' ')
                .Replace('_', ' ')
                .Replace("~~", string.Empty)
                .Replace('`', ' ');
        }

        private static string CleanupWhitespace(string text)
        {
            var normalized = MultiWhitespaceRegex.Replace(text, " ");
            normalized = SpaceBeforePunctuationRegex.Replace(normalized, "$1");
            normalized = SpaceAfterOpeningRegex.Replace(normalized, "$1");
            return normalized.Trim();
        }
    }
}
