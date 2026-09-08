using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Extraction
{
    /// <summary>
    /// The application use-case: resolve the System Info field, walk every requested
    /// hierarchy and package the trees plus summary counts into an
    /// <see cref="ExtractionResult"/>. Knows nothing about transport or storage.
    /// </summary>
    public sealed class ExtractionService
    {
        private readonly ISystemInfoFieldResolver _fieldResolver;
        private readonly IFieldCatalog _fieldCatalog;
        private readonly HierarchyWalker _walker;
        private readonly ISystemClock _clock;
        private readonly ILogger<ExtractionService> _logger;

        public ExtractionService(
            ISystemInfoFieldResolver fieldResolver,
            IFieldCatalog fieldCatalog,
            HierarchyWalker walker,
            ISystemClock clock,
            ILogger<ExtractionService> logger)
        {
            _fieldResolver = fieldResolver ?? throw new ArgumentNullException(nameof(fieldResolver));
            _fieldCatalog = fieldCatalog ?? throw new ArgumentNullException(nameof(fieldCatalog));
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ExtractionResult> ExtractAsync(
            ExtractionRequest request,
            IProgressListener progress,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            progress = progress ?? NullProgressListener.Instance;

            progress.Report(0, "Connecting to TFS...");
            var fieldRef = await _fieldResolver.ResolveReferenceNameAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Resolved System Info field to reference name {ReferenceName}", fieldRef);
            progress.Report(0, $"System Info field: {fieldRef}");

            var roots = await _walker.WalkAsync(request.RootIds, progress, cancellationToken).ConfigureAwait(false);

            int processed = 0, failed = 0, withInfo = 0;
            var fieldRefsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in roots.Flatten())
            {
                processed++;
                if (node.Error != null) failed++;
                else if (node.HasSystemInfo) withInfo++;

                if (node.Fields != null)
                {
                    foreach (var key in node.Fields.Keys)
                    {
                        fieldRefsSeen.Add(key);
                    }
                }
            }

            var availableFields = await ResolveAvailableFieldsAsync(fieldRefsSeen, cancellationToken).ConfigureAwait(false);

            progress.Report(0, $"Processed {processed} work items ({withInfo} with System Info, {failed} failed).");
            return new ExtractionResult(_clock.Now, roots, processed, failed, withInfo, availableFields);
        }

        private async Task<IReadOnlyList<FieldDefinition>> ResolveAvailableFieldsAsync(
            ISet<string> referenceNames,
            CancellationToken cancellationToken)
        {
            if (referenceNames.Count == 0)
            {
                return Array.Empty<FieldDefinition>();
            }

            var catalog = await _fieldCatalog.GetFieldsAsync(cancellationToken).ConfigureAwait(false);
            var byRef = catalog.ToDictionary(f => f.ReferenceName, StringComparer.OrdinalIgnoreCase);

            return referenceNames
                .Select(name => byRef.TryGetValue(name, out var def) ? def : new FieldDefinition(name, name))
                .OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }
}
