using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// Reads a single work item (and its child link ids) from the backing store.
    /// Implementations throw <see cref="Exceptions.WorkItemAccessException"/> when an
    /// item cannot be read (not found, forbidden, network, protocol).
    /// </summary>
    public interface IWorkItemSource
    {
        Task<RawWorkItem> GetAsync(int id, CancellationToken cancellationToken);
    }
}
