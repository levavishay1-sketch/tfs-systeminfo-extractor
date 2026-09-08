using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Jobs;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>
    /// Serves a finished job's <c>ExtractionResult</c> as JSON - the browser's data
    /// feed, and (with <c>?download=1</c>) the raw "Download JSON" action. Serialized
    /// on request; nothing is written to disk.
    /// </summary>
    public sealed class ResultEndpoint : IHttpEndpoint
    {
        private readonly IJobStore _store;
        private readonly JsonExportFormatter _json;

        public ResultEndpoint(IJobStore store, JsonExportFormatter json)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "GET" && path == "/api/result";

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var jobId = context.Request.QueryString["jobId"];
            if (jobId == null || !_store.TryGet(jobId, out var job) || job.Result == null)
            {
                return ResponseWriter.WriteStatusAsync(context.Response, 404);
            }

            var artifact = _json.Render(job.Result);

            return context.Request.QueryString["download"] == "1"
                ? ResponseWriter.WriteAttachmentAsync(context.Response, artifact.FileName, artifact.ContentType, artifact.Content, cancellationToken)
                : ResponseWriter.WriteContentAsync(context.Response, artifact.ContentType, artifact.Content, cancellationToken);
        }
    }
}
