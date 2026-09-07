using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TfsSystemInfoExtractor.Tests.Fakes
{
    /// <summary>Routes requests by a substring of the URL to a canned (status, json) response.</summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<(string UrlContains, HttpStatusCode Status, string Body)> _routes =
            new List<(string, HttpStatusCode, string)>();

        public List<string> RequestedUrls { get; } = new List<string>();

        public StubHttpMessageHandler Map(string urlContains, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((urlContains, status, json));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            foreach (var route in _routes)
            {
                if (url.Contains(route.UrlContains))
                {
                    return Task.FromResult(new HttpResponseMessage(route.Status)
                    {
                        Content = new StringContent(route.Body, System.Text.Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
