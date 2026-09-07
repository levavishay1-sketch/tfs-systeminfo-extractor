using System;
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
        private readonly HierarchyWalker _walker;
        private readonly ISystemClock _clock;
        private readonly ILogger<ExtractionService> _logger;

        public ExtractionService(
            ISystemInfoFieldResolver fieldResolver,
            HierarchyWalker walker,
            ISystemClock clock,
            ILogger<ExtractionService> logger)
        {
            _fieldResolver = fieldResolver ?? throw new ArgumentNullException(nameof(fieldResolver));
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
            foreach (var node in roots.Flatten())
            {
                processed++;
                if (node.Error != null) failed++;
                else if (node.HasSystemInfo) withInfo++;
            }

            progress.Report(0, $"Processed {processed} work items ({withInfo} with System Info, {failed} failed).");
            return new ExtractionResult(_clock.Now, roots, processed, failed, withInfo);
        }
    }
}
