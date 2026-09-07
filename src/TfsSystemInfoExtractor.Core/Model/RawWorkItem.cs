using System.Collections.Generic;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>
    /// The infrastructure-agnostic projection of a TFS work item that
    /// <see cref="Abstractions.IWorkItemSource"/> returns. System Info is still
    /// raw HTML at this point; the child link targets are already resolved to ids.
    /// </summary>
    public sealed class RawWorkItem
    {
        public RawWorkItem(
            int id,
            string? type,
            string? title,
            string? state,
            string? webUrl,
            string? systemInfoHtml,
            IReadOnlyList<int> childIds)
        {
            Id = id;
            Type = type;
            Title = title;
            State = state;
            WebUrl = webUrl;
            SystemInfoHtml = systemInfoHtml;
            ChildIds = childIds;
        }

        public int Id { get; }

        public string? Type { get; }

        public string? Title { get; }

        public string? State { get; }

        public string? WebUrl { get; }

        public string? SystemInfoHtml { get; }

        public IReadOnlyList<int> ChildIds { get; }
    }
}
