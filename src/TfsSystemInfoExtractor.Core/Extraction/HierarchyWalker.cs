using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Extraction
{
    /// <summary>
    /// Depth-first walk of a work item hierarchy. Each item is fetched once — if the
    /// same child appears under several parents it is expanded only under the first —
    /// and an item that fails to load becomes a node carrying only <see cref="WorkItemNode.Error"/>.
    /// The walker has no knowledge of HTTP, TFS or files; it composes an
    /// <see cref="IWorkItemSource"/> and an <see cref="IHtmlToText"/>.
    /// </summary>
    public sealed class HierarchyWalker
    {
        private readonly IWorkItemSource _source;
        private readonly IHtmlToText _htmlToText;
        private readonly int _maxDepth;
        private int _processed;

        public HierarchyWalker(IWorkItemSource source, IHtmlToText htmlToText, IOptions<ExtractionOptions> options)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _htmlToText = htmlToText ?? throw new ArgumentNullException(nameof(htmlToText));
            _maxDepth = Math.Max(1, (options?.Value ?? new ExtractionOptions()).MaxDepth);
        }

        public async Task<IReadOnlyList<WorkItemNode>> WalkAsync(
            IEnumerable<int> rootIds,
            IProgressListener progress,
            CancellationToken cancellationToken)
        {
            if (rootIds == null) throw new ArgumentNullException(nameof(rootIds));
            progress = progress ?? NullProgressListener.Instance;

            _processed = 0;
            var visited = new HashSet<int>();
            var roots = new List<WorkItemNode>();

            foreach (var id in rootIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(0, $"Processing root work item {id}");
                var node = await ProcessAsync(id, 0, visited, progress, cancellationToken).ConfigureAwait(false);
                if (node != null)
                {
                    roots.Add(node);
                }
            }

            return roots;
        }

        private async Task<WorkItemNode?> ProcessAsync(
            int id,
            int depth,
            HashSet<int> visited,
            IProgressListener progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(id))
            {
                progress.Report(depth, $"Work item {id} already processed - skipping duplicate.");
                return null;
            }

            if (depth >= _maxDepth)
            {
                progress.Report(depth, $"Reached max depth ({_maxDepth}); not expanding work item {id}.");
                progress.ItemProcessed(++_processed);
                return new WorkItemNode { Id = id, Error = $"Not expanded: hierarchy exceeded the configured maximum depth of {_maxDepth}." };
            }

            progress.Report(depth, $"Loading work item {id}...");

            RawWorkItem raw;
            try
            {
                raw = await _source.GetAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (WorkItemAccessException ex)
            {
                progress.Report(depth, $"[ERROR] {ex.Message}");
                progress.ItemProcessed(++_processed);
                return new WorkItemNode { Id = id, Error = ex.Message };
            }

            var node = new WorkItemNode
            {
                Id = raw.Id,
                Type = raw.Type,
                Title = raw.Title,
                State = raw.State,
                Url = raw.WebUrl,
                SystemInfo = _htmlToText.Convert(raw.SystemInfoHtml)
            };

            progress.Report(depth, node.HasSystemInfo ? "[System Info found]" : "[no System Info]");
            progress.Report(depth, $"Children: {raw.ChildIds.Count}");
            progress.ItemProcessed(++_processed);

            foreach (var childId in raw.ChildIds)
            {
                var childNode = await ProcessAsync(childId, depth + 1, visited, progress, cancellationToken).ConfigureAwait(false);
                if (childNode != null)
                {
                    node.Children.Add(childNode);
                }
            }

            return node;
        }
    }
}
