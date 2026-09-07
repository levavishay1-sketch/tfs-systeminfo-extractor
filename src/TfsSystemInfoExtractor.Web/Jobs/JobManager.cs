using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Web.Jobs
{
    public interface IJobManager
    {
        ExtractionJob Start(IReadOnlyList<int> workItemIds);
    }

    /// <summary>
    /// Owns the lifecycle of a background extraction: creates the <see cref="ExtractionJob"/>,
    /// runs <see cref="ExtractionService"/> inside its own DI scope, renders every export
    /// format into the job and persists each artifact to disk, then flips the job's status.
    /// </summary>
    public sealed class JobManager : IJobManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IJobStore _store;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<JobManager> _logger;

        public JobManager(
            IServiceScopeFactory scopeFactory,
            IJobStore store,
            ILoggerFactory loggerFactory,
            ILogger<JobManager> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ExtractionJob Start(IReadOnlyList<int> workItemIds)
        {
            var job = new ExtractionJob(Guid.NewGuid().ToString("N"));
            _store.Add(job);
            _ = Task.Run(() => RunAsync(job, workItemIds));
            return job;
        }

        private async Task RunAsync(ExtractionJob job, IReadOnlyList<int> workItemIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;

                var extraction = services.GetRequiredService<ExtractionService>();
                var formatters = services.GetRequiredService<ExportFormatterSelector>();
                var store = services.GetRequiredService<IExtractionArtifactStore>();

                var progress = new JobProgressListener(job, _loggerFactory.CreateLogger("Extraction"));

                var result = await extraction
                    .ExtractAsync(new ExtractionRequest(workItemIds), progress, CancellationToken.None)
                    .ConfigureAwait(false);

                var artifacts = new Dictionary<ExportFormat, ExportArtifact>();
                foreach (var formatter in formatters.All)
                {
                    var artifact = formatter.Render(result);
                    artifacts[artifact.Format] = artifact;
                    var path = await store.SaveAsync(artifact, CancellationToken.None).ConfigureAwait(false);
                    progress.Report(0, $"{artifact.Format}: {path}");
                }

                job.Complete(artifacts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Extraction job {JobId} failed", job.Id);
                job.AppendLog(0, $"[FATAL] {ex.Message}");
                job.Fail(ex.Message);
            }
        }
    }
}
