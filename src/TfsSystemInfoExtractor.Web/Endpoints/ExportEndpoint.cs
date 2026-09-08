using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Web.Contracts;
using TfsSystemInfoExtractor.Web.Http;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>
    /// <c>POST /api/export</c> - the single, on-demand export action. The browser posts
    /// the format token and the exact view it is currently showing; the matching
    /// <see cref="IReportRenderer"/> turns that into a file which is streamed back and
    /// also saved to the export folder. Nothing is generated until this is called.
    /// </summary>
    public sealed class ExportEndpoint : IHttpEndpoint
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ReportRendererSelector _renderers;
        private readonly IExtractionArtifactStore _store;
        private readonly ILogger<ExportEndpoint> _logger;

        public ExportEndpoint(ReportRendererSelector renderers, IExtractionArtifactStore store, ILogger<ExportEndpoint> logger)
        {
            _renderers = renderers ?? throw new ArgumentNullException(nameof(renderers));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "POST" && path == "/api/export";

        public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            ExportRequest? request;
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                request = JsonSerializer.Deserialize<ExportRequest>(body, JsonOptions);
            }
            catch (Exception ex)
            {
                await ResponseWriter.WriteTextAsync(context.Response, "Malformed export request: " + ex.Message, 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (request?.View == null || request.View.Columns.Count == 0)
            {
                await ResponseWriter.WriteTextAsync(context.Response, "Export request has no view.", 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!_renderers.TryResolve(request.Format, out var renderer))
            {
                await ResponseWriter.WriteTextAsync(context.Response, $"Unknown export format '{request.Format}'.", 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var artifact = await Task.Run(() => renderer.Render(request.View), cancellationToken).ConfigureAwait(false);
                await _store.SaveAsync(artifact, cancellationToken).ConfigureAwait(false);
                await ResponseWriter.WriteAttachmentAsync(context.Response, artifact.FileName, artifact.ContentType, artifact.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (BrowserNotFoundException ex)
            {
                await ResponseWriter.WriteTextAsync(context.Response, ex.Message, 422, cancellationToken).ConfigureAwait(false);
            }
            catch (PdfRenderException ex)
            {
                _logger.LogError(ex, "PDF export failed");
                await ResponseWriter.WriteTextAsync(context.Response, ex.Message, 500, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
