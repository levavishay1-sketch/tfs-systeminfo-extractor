using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Ui;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>Serves the single-page browser UI.</summary>
    public sealed class IndexEndpoint : IHttpEndpoint
    {
        private readonly IUiAssetProvider _ui;

        public IndexEndpoint(IUiAssetProvider ui) => _ui = ui ?? throw new ArgumentNullException(nameof(ui));

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "GET" && path == "/";

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken) =>
            ResponseWriter.WriteHtmlAsync(context.Response, _ui.IndexHtml, cancellationToken);
    }
}
