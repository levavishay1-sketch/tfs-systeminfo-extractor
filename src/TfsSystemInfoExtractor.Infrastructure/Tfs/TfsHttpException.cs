using System;
using System.Net;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>A TFS REST call returned a non-success status code. Callers translate this to a domain exception.</summary>
    internal sealed class TfsHttpException : Exception
    {
        public TfsHttpException(HttpStatusCode statusCode, string requestUrl, string responseBody)
            : base($"TFS returned {(int)statusCode} ({statusCode}) for {requestUrl}. {Trim(responseBody)}")
        {
            StatusCode = statusCode;
            RequestUrl = requestUrl;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }

        public string RequestUrl { get; }

        public string ResponseBody { get; }

        private static string Trim(string body) =>
            string.IsNullOrEmpty(body) ? string.Empty
            : body.Length <= 500 ? body
            : body.Substring(0, 500) + "...";
    }
}
