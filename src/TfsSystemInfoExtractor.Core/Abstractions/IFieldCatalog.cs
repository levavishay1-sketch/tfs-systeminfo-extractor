using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// The collection's work item field catalogue (from <c>_apis/wit/fields</c>).
    /// Implementations cache the result for the lifetime of a run.
    /// </summary>
    public interface IFieldCatalog
    {
        Task<IReadOnlyList<FieldDefinition>> GetFieldsAsync(CancellationToken cancellationToken);
    }
}
