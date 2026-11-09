using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VKClient.Common.Utils
{
    public static class OpenVKMentions
    {
        private const string BracketPattern = @"\[(?<target>[a-z0-9_\-]+)\|(?<label>[^\]\r\n]*)\]|(?<![\w@/\\.:|])[@*](?<legacy>(?:id|club|event|public)\d+)\s*\((?<name>[^)\r\n]+)\)";
        private const string HandlePattern = @"(?<![\w@/\\.:|])@(?<handle>[a-z0-9_]+(?:-[a-z0-9_]+)*)(?![a-z0-9_\-])";
        private static readonly Regex Brackets = new Regex(BracketPattern, RegexOptions.IgnoreCase);
        private static readonly Regex Handles = new Regex(HandlePattern, RegexOptions.IgnoreCase);
        public static readonly Regex Tokens = new Regex(BracketPattern + "|" + HandlePattern, RegexOptions.IgnoreCase);

        public static string LinkBrackets(string text, string origin)
        {
            return Brackets.Replace(text, match => {
                string target = match.Groups["target"].Success ? match.Groups["target"].Value : match.Groups["legacy"].Value;
                string label = match.Groups["label"].Success ? match.Groups["label"].Value : match.Groups["name"].Value;
                return Link(origin, target, label.Length == 0 ? target : label);
            });
        }
        public static string LinkHandles(string text, string origin)
        {
            return Handles.Replace(text, match => Link(origin, match.Groups["handle"].Value, match.Value));
        }
        private static string Link(string origin, string target, string label)
        {
            return "\a" + origin.TrimEnd('/') + "/" + target + "\b" + label + "\b\a";
        }

        public static string CreateMention(long id, string name)
        {
            if (id == 0) throw new ArgumentOutOfRangeException("id");
            string number = id.ToString(CultureInfo.InvariantCulture);
            string target = id > 0 ? "id" + number : "club" + number.Substring(1);
            string label = Regex.Replace(name ?? "", @"[\[\]|\p{Cc}]", " ");
            label = Regex.Replace(label, @"\s+", " ").Trim();
            return "[" + target + "|" + (label.Length == 0 ? target : label) + "]";
        }
        private static bool QueryCharacter(char value) { return char.IsLetterOrDigit(value) || value == '_' || value == '-'; }
        public static bool TryGetQuery(string text, int caret, int selectionLength, out int start, out string query)
        {
            start = -1; query = null;
            if (text == null || selectionLength != 0 || caret <= 0 || caret > text.Length) return false;
            int index = caret - 1;
            while (index >= 0 && QueryCharacter(text[index])) index--;
            if (index < 0 || text[index] != '@') return false;
            if (index > 0 && !char.IsWhiteSpace(text[index - 1]) && "([{,;!?\"'«»".IndexOf(text[index - 1]) < 0) return false;
            start = index; query = text.Substring(index + 1, caret - index - 1); return true;
        }
        public static bool TryInsert(string text, int caret, int selectionLength, long id, string name, out string result, out int nextCaret)
        {
            result = text; nextCaret = caret;
            int start; string query;
            if (!TryGetQuery(text, caret, selectionLength, out start, out query) || id == 0) return false;
            int end = caret;
            while (end < text.Length && QueryCharacter(text[end])) end++;
            string mention = CreateMention(id, name) + (end == text.Length ? " " : "");
            result = text.Remove(start, end - start).Insert(start, mention);
            nextCaret = start + mention.Length; return true;
        }
    }
}
