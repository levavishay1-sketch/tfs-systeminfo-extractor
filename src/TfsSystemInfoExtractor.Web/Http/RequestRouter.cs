using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TfsSystemInfoExtractor.Web.Http
{
    /// <summary>Dispatches a request to the first matching <see cref="IHttpEndpoint"/>; 404 otherwise, 500 on an unhandled error.</summary>
    public sealed class RequestRouter
    {
        private readonly IReadOnlyList<IHttpEndpoint> _endpoints;
        private readonly ILogger<RequestRouter> _logger;

        public RequestRouter(IEnumerable<IHttpEndpoint> endpoints, ILogger<RequestRouter> logger)
        {
            _endpoints = endpoints?.ToArray() ?? throw new ArgumentNullException(nameof(endpoints));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task DispatchAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var method = context.Request.HttpMethod;
            var path = context.Request.Url?.AbsolutePath ?? "/";

            try
            {
                var endpoint = _endpoints.FirstOrDefault(e => e.Matches(method, path));
                if (endpoint == null)
                {
                    await ResponseWriter.WriteStatusAsync(context.Response, 404).ConfigureAwait(false);
                    return;
                }

                await endpoint.HandleAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error handling {Method} {Path}", method, path);
                TryWriteError(context);
            }
        }

        private static void TryWriteError(HttpListenerContext context)
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // the response may already be (partly) written; nothing more we can do.
            }
        }
    }
}
