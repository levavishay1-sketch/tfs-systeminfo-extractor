using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Jobs;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>Streams a finished job's rendered export (json / md / csv) as a download.</summary>
    public sealed class DownloadArtifactEndpoint : IHttpEndpoint
    {
        private readonly IJobStore _store;
        private readonly ExportFormatterSelector _formatters;

        public DownloadArtifactEndpoint(IJobStore store, ExportFormatterSelector formatters)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _formatters = formatters ?? throw new ArgumentNullException(nameof(formatters));
        }

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "GET" && path == "/api/download";

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var jobId = context.Request.QueryString["jobId"];
            var type = context.Request.QueryString["type"] ?? "json";

            if (jobId == null || !_store.TryGet(jobId, out var job))
            {
                return ResponseWriter.WriteStatusAsync(context.Response, 404);
            }

            if (!_formatters.TryResolve(type, out var formatter) || !job.Artifacts.TryGetValue(formatter.Format, out var artifact))
            {
                return ResponseWriter.WriteTextAsync(context.Response, "Export not ready yet.", 404, cancellationToken);
            }

            return ResponseWriter.WriteAttachmentAsync(context.Response, artifact.FileName, artifact.ContentType, artifact.Content, cancellationToken);
        }
    }
}
