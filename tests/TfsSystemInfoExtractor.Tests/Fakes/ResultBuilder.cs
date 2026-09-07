using System;
using System.Collections.Generic;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Tests.Fakes
{
    internal static class ResultBuilder
    {
        public static WorkItemNode Node(int id, string? type = "Task", string? title = null, string? state = "Active", string? systemInfo = null, string? error = null)
        {
            return new WorkItemNode
            {
                Id = id,
                Type = type,
                Title = title ?? $"Item {id}",
                State = state,
                Url = $"http://tfs/{id}",
                SystemInfo = systemInfo,
                Error = error
            };
        }

        public static ExtractionResult Result(params WorkItemNode[] roots)
        {
            int processed = 0, failed = 0, withInfo = 0;
            foreach (var node in roots.Flatten())
            {
                processed++;
                if (node.Error != null) failed++;
                else if (node.HasSystemInfo) withInfo++;
            }

            return new ExtractionResult(
                new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                roots,
                processed,
                failed,
                withInfo);
        }

        public static WorkItemNode WithChildren(this WorkItemNode node, params WorkItemNode[] children)
        {
            foreach (var child in children)
            {
                node.Children.Add(child);
            }

            return node;
        }
    }
}
