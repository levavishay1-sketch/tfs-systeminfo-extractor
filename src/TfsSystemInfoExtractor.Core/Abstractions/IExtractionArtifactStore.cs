using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>Persists a rendered export somewhere durable and returns where it was written.</summary>
    public interface IExtractionArtifactStore
    {
        Task<string> SaveAsync(ExportArtifact artifact, CancellationToken cancellationToken);
    }
}
