using System.Collections.Generic;
using TfsSystemInfoExtractor.Core.Model.SourceControl;

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
            IReadOnlyList<int> childIds,
            SourceControlInfo? sourceControl = null,
            IReadOnlyDictionary<string, string>? fields = null)
        {
            Id = id;
            Type = type;
            Title = title;
            State = state;
            WebUrl = webUrl;
            SystemInfoHtml = systemInfoHtml;
            ChildIds = childIds;
            SourceControl = sourceControl;
            Fields = fields;
        }

        public int Id { get; }

        public string? Type { get; }

        public string? Title { get; }

        public string? State { get; }

        public string? WebUrl { get; }

        public string? SystemInfoHtml { get; }

        public IReadOnlyList<int> ChildIds { get; }

        /// <summary>Source-control activity for the item, when the source provided it; otherwise null.</summary>
        public SourceControlInfo? SourceControl { get; }

        /// <summary>Extra field values keyed by reference name; null or empty when none.</summary>
        public IReadOnlyDictionary<string, string>? Fields { get; }
    }
}
