using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TfsSystemInfoExtractor.Web.Http
{
    /// <summary>
    /// One place for writing an <see cref="HttpListenerResponse"/> body. Replaces the
    /// three near-identical WriteHtml / WriteJson / WriteText helpers the old server
    /// carried and centralises status codes and content types.
    /// </summary>
    public static class ResponseWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static Task WriteJsonAsync<T>(HttpListenerResponse response, T payload, int statusCode = 200, CancellationToken cancellationToken = default)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            return WriteAsync(response, statusCode, "application/json; charset=utf-8", bytes, cancellationToken);
        }

        public static Task WriteHtmlAsync(HttpListenerResponse response, byte[] html, CancellationToken cancellationToken = default) =>
            WriteAsync(response, 200, "text/html; charset=utf-8", html, cancellationToken);

        public static Task WriteTextAsync(HttpListenerResponse response, string text, int statusCode = 200, CancellationToken cancellationToken = default) =>
            WriteAsync(response, statusCode, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text ?? string.Empty), cancellationToken);

        public static Task WriteAttachmentAsync(HttpListenerResponse response, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
        {
            response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            return WriteAsync(response, 200, contentType, content, cancellationToken);
        }

        public static Task WriteStatusAsync(HttpListenerResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.Close();
            return Task.CompletedTask;
        }

        private static async Task WriteAsync(HttpListenerResponse response, int statusCode, string contentType, byte[] body, CancellationToken cancellationToken)
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(false);
            response.Close();
        }
    }
}
