using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Web.Contracts;
using TfsSystemInfoExtractor.Web.Http;

namespace TfsSystemInfoExtractor.Web.Endpoints
{
    /// <summary>
    /// <c>POST /api/tfs-credentials</c> - the sign-in dialog posts the username / password
    /// here after a run hit 401. They are handed to the credential store (in-memory only)
    /// and the browser then re-runs. The request body is never logged.
    /// </summary>
    public sealed class TfsCredentialsEndpoint : IHttpEndpoint
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly ITfsCredentialPrompt _credentials;
        private readonly ILogger<TfsCredentialsEndpoint> _logger;

        public TfsCredentialsEndpoint(ITfsCredentialPrompt credentials, ILogger<TfsCredentialsEndpoint> logger)
        {
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Matches(string httpMethod, string path) =>
            httpMethod == "POST" && path == "/api/tfs-credentials";

        public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            CredentialsRequest? request;
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                request = JsonSerializer.Deserialize<CredentialsRequest>(body, JsonOptions);
            }
            catch (Exception)
            {
                // deliberately no exception detail - the body may contain the password
                await ResponseWriter.WriteTextAsync(context.Response, "Malformed credentials request.", 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
            {
                await ResponseWriter.WriteTextAsync(context.Response, "Username and password are required.", 400, cancellationToken).ConfigureAwait(false);
                return;
            }

            _credentials.Set(request.UserName, request.Password);
            _logger.LogInformation("Azure DevOps credentials updated for user {User}; retrying.", request.UserName);
            request.Password = string.Empty; // drop it as soon as it is handed off

            await ResponseWriter.WriteStatusAsync(context.Response, 204).ConfigureAwait(false);
        }
    }
}
