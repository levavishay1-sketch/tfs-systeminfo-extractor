using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// Resolves the source-control activity (commits, repositories, contributors,
    /// components) behind a work item's artifact links. Implementations call the
    /// relevant version-control APIs; a failure to resolve one link must not fail the
    /// whole call - return whatever could be resolved.
    /// </summary>
    public interface IWorkItemSourceControlProvider
    {
        Task<SourceControlInfo> GetAsync(
            int workItemId,
            IReadOnlyList<WorkItemArtifactLink> artifactLinks,
            CancellationToken cancellationToken);
    }
}
