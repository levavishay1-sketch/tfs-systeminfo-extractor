using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using TfsSystemInfoExtractor.Core.Abstractions;

namespace TfsSystemInfoExtractor.Infrastructure.Text
{
    /// <summary>
    /// Converts the rich-text HTML that TFS stores in fields like "System Info" to
    /// tidy plain text: block tags become newlines, all other tags are stripped,
    /// HTML entities are decoded, and runs of blank lines are collapsed.
    /// </summary>
    public sealed class HtmlToPlainTextConverter : IHtmlToText
    {
        private static readonly Regex LineBreak = new Regex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BlockClose = new Regex(@"<\s*/\s*(p|div|li)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BlockOpen = new Regex(@"<\s*(p|div|li)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        public string Convert(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var text = LineBreak.Replace(html, "\n");
            text = BlockClose.Replace(text, "\n");
            text = BlockOpen.Replace(text, string.Empty);
            text = AnyTag.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text);

            var lines = text
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .SkipWhile(string.IsNullOrEmpty);

            var result = new List<string>();
            var lastWasBlank = false;
            foreach (var line in lines)
            {
                var blank = line.Length == 0;
                if (blank && lastWasBlank)
                {
                    continue;
                }

                result.Add(line);
                lastWasBlank = blank;
            }

            while (result.Count > 0 && result[result.Count - 1].Length == 0)
            {
                result.RemoveAt(result.Count - 1);
            }

            return string.Join("\n", result);
        }
    }
}
