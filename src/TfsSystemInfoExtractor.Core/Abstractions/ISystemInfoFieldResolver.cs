using System.Threading;
using System.Threading.Tasks;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// Resolves the technical reference name of the "System Info" field from its
    /// display name, so the field never has to be hard-coded. Implementations are
    /// expected to cache the answer for the lifetime of a run.
    /// </summary>
    public interface ISystemInfoFieldResolver
    {
        Task<string> ResolveReferenceNameAsync(CancellationToken cancellationToken);
    }
}
