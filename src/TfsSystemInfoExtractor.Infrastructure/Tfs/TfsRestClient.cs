using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// Thin transport over the TFS REST API: builds collection-relative URLs (adding
    /// <c>api-version</c>), issues the GET with Windows integrated auth, and turns
    /// transport failures into <see cref="TfsUnreachableException"/> / <see cref="TfsHttpException"/>.
    /// It performs no work-item-specific interpretation.
    /// </summary>
    public sealed class TfsRestClient
    {
        public const string HttpClientName = "tfs";

        private readonly HttpClient _http;
        private readonly TfsOptions _options;

        public TfsRestClient(HttpClient http, IOptions<TfsOptions> options)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        }

        /// <param name="collectionRelativePath">e.g. <c>_apis/wit/fields</c> or <c>_apis/wit/workitems/42</c>.</param>
        /// <param name="query">extra query string without a leading '?', or null.</param>
        public async Task<JsonDocument> GetJsonAsync(string collectionRelativePath, string? query, CancellationToken cancellationToken)
        {
            var url = BuildUrl(collectionRelativePath, query);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new TfsUnreachableException(
                    $"Could not reach TFS at {_options.CollectionUrl}. Check the URL, network access and Windows authentication. Details: {ex.Message}",
                    ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TfsUnreachableException($"The request to {url} timed out.", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new TfsHttpException(response.StatusCode, url, body);
                }

                try
                {
                    return JsonDocument.Parse(body);
                }
                catch (JsonException ex)
                {
                    throw new TfsHttpException(response.StatusCode, url, $"Response was not valid JSON: {ex.Message}");
                }
            }
        }

        private string BuildUrl(string collectionRelativePath, string? query)
        {
            var baseUrl = _options.CollectionUrl.TrimEnd('/');
            var path = collectionRelativePath.TrimStart('/');
            var separator = string.IsNullOrEmpty(query) ? string.Empty : "&" + query;
            return $"{baseUrl}/{path}?api-version={_options.ApiVersion}{separator}";
        }
    }
}
