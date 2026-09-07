using System.Collections.Generic;

namespace TfsSystemInfoExtractor.Core.Model
{
    public static class WorkItemNodeExtensions
    {
        /// <summary>Enumerates the forest depth-first, each node followed by its descendants.</summary>
        public static IEnumerable<WorkItemNode> Flatten(this IEnumerable<WorkItemNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var descendant in node.Children.Flatten())
                {
                    yield return descendant;
                }
            }
        }
    }
}
