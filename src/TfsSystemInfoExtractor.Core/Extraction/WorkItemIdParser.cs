using System;
using System.Collections.Generic;

namespace TfsSystemInfoExtractor.Core.Extraction
{
    /// <summary>
    /// Parses a free-text blob (pasted or uploaded) into a de-duplicated, order-preserving
    /// list of work item ids. Accepts commas, semicolons and any whitespace as separators.
    /// </summary>
    public sealed class WorkItemIdParser
    {
        private static readonly char[] Separators = { ',', ';', '\n', '\r', '\t', ' ' };

        public IReadOnlyList<int> Parse(string? raw)
        {
            var ids = new List<int>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ids;
            }

            var seen = new HashSet<int>();
            foreach (var part in raw!.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var id) && id > 0 && seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
    }
}
