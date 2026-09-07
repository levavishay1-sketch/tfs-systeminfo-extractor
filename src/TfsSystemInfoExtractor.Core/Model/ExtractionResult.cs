using System;
using System.Collections.Generic;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>
    /// The outcome of an extraction run: the hierarchy trees plus summary counts.
    /// This is the single object every export formatter renders from.
    /// </summary>
    public sealed class ExtractionResult
    {
        public ExtractionResult(
            DateTimeOffset generatedAt,
            IReadOnlyList<WorkItemNode> roots,
            int processedCount,
            int failedCount,
            int withSystemInfoCount)
        {
            GeneratedAt = generatedAt;
            Roots = roots;
            ProcessedCount = processedCount;
            FailedCount = failedCount;
            WithSystemInfoCount = withSystemInfoCount;
        }

        public DateTimeOffset GeneratedAt { get; }

        public IReadOnlyList<WorkItemNode> Roots { get; }

        /// <summary>Total number of distinct work items visited (including failed ones).</summary>
        public int ProcessedCount { get; }

        public int FailedCount { get; }

        public int WithSystemInfoCount { get; }
    }
}
