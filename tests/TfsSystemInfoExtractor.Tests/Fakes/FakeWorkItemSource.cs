using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;

namespace TfsSystemInfoExtractor.Tests.Fakes
{
    /// <summary>In-memory <see cref="IWorkItemSource"/> for walker tests. Records how many times each id was fetched.</summary>
    internal sealed class FakeWorkItemSource : IWorkItemSource
    {
        private readonly Dictionary<int, RawWorkItem> _items = new Dictionary<int, RawWorkItem>();
        private readonly HashSet<int> _forbidden = new HashSet<int>();

        public Dictionary<int, int> FetchCounts { get; } = new Dictionary<int, int>();

        public FakeWorkItemSource Add(int id, string? systemInfoHtml = null, params int[] childIds)
        {
            _items[id] = new RawWorkItem(id, "Task", $"Item {id}", "Active", $"http://tfs/{id}", systemInfoHtml, childIds);
            return this;
        }

        public FakeWorkItemSource Add(int id, SourceControlInfo sourceControl, params int[] childIds)
        {
            _items[id] = new RawWorkItem(id, "Task", $"Item {id}", "Active", $"http://tfs/{id}", null, childIds, sourceControl);
            return this;
        }

        public FakeWorkItemSource Forbid(int id)
        {
            _forbidden.Add(id);
            return this;
        }

        public Task<RawWorkItem> GetAsync(int id, CancellationToken cancellationToken)
        {
            FetchCounts[id] = FetchCounts.TryGetValue(id, out var n) ? n + 1 : 1;

            if (_forbidden.Contains(id))
            {
                throw new WorkItemAccessException(id, WorkItemAccessError.Forbidden, $"No permission to read work item {id} (403).");
            }

            if (!_items.TryGetValue(id, out var item))
            {
                throw new WorkItemAccessException(id, WorkItemAccessError.NotFound, $"Work item {id} does not exist (404).");
            }

            return Task.FromResult(item);
        }
    }
}
