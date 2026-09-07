using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Web.Contracts;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Jobs;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>Starts a background extraction from the pasted / uploaded work item ids.</summary>
    public sealed class StartExtractionEndpoint : IHttpEndpoint
    {
        private readonly WorkItemIdParser _parser;
        private readonly IJobManager _jobManager;

        public StartExtractionEndpoint(WorkItemIdParser parser, IJobManager jobManager)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
        }

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "POST" && path == "/api/run";

        public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var ids = _parser.Parse(body);
            if (ids.Count == 0)
            {
                await ResponseWriter.WriteTextAsync(context.Response, "No valid Work Item IDs found in input.", 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            var job = _jobManager.Start(ids);
            await ResponseWriter.WriteJsonAsync(context.Response, new StartResponse(job.Id), 200, cancellationToken).ConfigureAwait(false);
        }
    }
}
