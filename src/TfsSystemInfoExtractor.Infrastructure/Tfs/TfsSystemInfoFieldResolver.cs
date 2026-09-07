using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// Resolves the "System Info" field's technical reference name from the collection's
    /// field catalogue, by display name. Registered per run (scoped): the first call
    /// hits TFS, later calls reuse the cached answer.
    /// </summary>
    public sealed class TfsSystemInfoFieldResolver : ISystemInfoFieldResolver
    {
        private readonly TfsRestClient _client;
        private readonly TfsOptions _options;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private string? _cached;

        public TfsSystemInfoFieldResolver(TfsRestClient client, IOptions<TfsOptions> options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        }

        public async Task<string> ResolveReferenceNameAsync(CancellationToken cancellationToken)
        {
            if (_cached != null)
            {
                return _cached;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cached != null)
                {
                    return _cached;
                }

                _cached = await FetchAsync(cancellationToken).ConfigureAwait(false);
                return _cached;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<string> FetchAsync(CancellationToken cancellationToken)
        {
            JsonDocument document;
            try
            {
                document = await _client.GetJsonAsync("_apis/wit/fields", query: null, cancellationToken).ConfigureAwait(false);
            }
            catch (TfsHttpException ex)
            {
                throw new TfsUnreachableException(
                    $"Could not read the TFS field definitions ({(int)ex.StatusCode} {ex.StatusCode}). {ex.ResponseBody}", ex);
            }

            using (document)
            {
                if (document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in values.EnumerateArray())
                    {
                        if (field.TryGetProperty("name", out var name) &&
                            string.Equals(name.GetString(), _options.SystemInfoFieldDisplayName, StringComparison.OrdinalIgnoreCase) &&
                            field.TryGetProperty("referenceName", out var referenceName))
                        {
                            return referenceName.GetString()
                                   ?? throw new SystemInfoFieldNotFoundException(_options.SystemInfoFieldDisplayName);
                        }
                    }
                }

                throw new SystemInfoFieldNotFoundException(_options.SystemInfoFieldDisplayName);
            }
        }
    }
}
