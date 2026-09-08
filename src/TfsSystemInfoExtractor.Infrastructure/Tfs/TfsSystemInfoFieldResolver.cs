using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// Resolves the "System Info" field's technical reference name from its display
    /// name, using the shared <see cref="IFieldCatalog"/> so the field list is fetched
    /// only once per run.
    /// </summary>
    public sealed class TfsSystemInfoFieldResolver : ISystemInfoFieldResolver
    {
        private readonly IFieldCatalog _catalog;
        private readonly TfsOptions _options;
        private string? _cached;

        public TfsSystemInfoFieldResolver(IFieldCatalog catalog, IOptions<TfsOptions> options)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        }

        public async Task<string> ResolveReferenceNameAsync(CancellationToken cancellationToken)
        {
            if (_cached != null)
            {
                return _cached;
            }

            var fields = await _catalog.GetFieldsAsync(cancellationToken).ConfigureAwait(false);
            var match = fields.FirstOrDefault(f =>
                string.Equals(f.DisplayName, _options.SystemInfoFieldDisplayName, StringComparison.OrdinalIgnoreCase));

            _cached = match?.ReferenceName
                      ?? throw new SystemInfoFieldNotFoundException(_options.SystemInfoFieldDisplayName);
            return _cached;
        }
    }
}
