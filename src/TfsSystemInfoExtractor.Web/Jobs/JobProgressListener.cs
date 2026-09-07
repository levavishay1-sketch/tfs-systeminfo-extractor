using System;
using Microsoft.Extensions.Logging;
using TfsSystemInfoExtractor.Core.Abstractions;

namespace TfsSystemInfoExtractor.Web.Jobs
{
    /// <summary>Bridges <see cref="IProgressListener"/> onto a single <see cref="ExtractionJob"/>'s log and counter, mirroring to the app log.</summary>
    public sealed class JobProgressListener : IProgressListener
    {
        private readonly ExtractionJob _job;
        private readonly ILogger _logger;

        public JobProgressListener(ExtractionJob job, ILogger logger)
        {
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Report(int depth, string message)
        {
            _job.AppendLog(depth, message);
            _logger.LogInformation("[{JobId}] {Message}", _job.Id, message);
        }

        public void ItemProcessed(int totalProcessed) => _job.SetProcessedCount(totalProcessed);
    }
}
