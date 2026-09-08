using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Web.Contracts;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Jobs;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>Reports live progress for a running or finished job (polled by the UI).</summary>
    public sealed class JobStatusEndpoint : IHttpEndpoint
    {
        private readonly IJobStore _store;

        public JobStatusEndpoint(IJobStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "GET" && path == "/api/status";

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var jobId = context.Request.QueryString["jobId"];
            if (jobId == null || !_store.TryGet(jobId, out var job))
            {
                return ResponseWriter.WriteStatusAsync(context.Response, 404);
            }

            var response = new StatusResponse(
                done: job.IsDone,
                processed: job.ProcessedCount,
                error: job.Error,
                log: job.LogSnapshot,
                hasResult: job.Status == JobStatus.Completed && job.HasResult);

            return ResponseWriter.WriteJsonAsync(context.Response, response, 200, cancellationToken);
        }
    }
}
