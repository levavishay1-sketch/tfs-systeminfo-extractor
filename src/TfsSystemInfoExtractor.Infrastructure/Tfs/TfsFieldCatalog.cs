using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// TFS-backed <see cref="IFieldCatalog"/>: reads <c>_apis/wit/fields</c> once per run
    /// and caches it. Both the System Info field resolver and the "extra columns"
    /// feature draw their field metadata from here rather than hard-coding anything.
    /// </summary>
    public sealed class TfsFieldCatalog : IFieldCatalog
    {
        private readonly TfsRestClient _client;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private IReadOnlyList<FieldDefinition>? _cache;

        public TfsFieldCatalog(TfsRestClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<IReadOnlyList<FieldDefinition>> GetFieldsAsync(CancellationToken cancellationToken)
        {
            if (_cache != null)
            {
                return _cache;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return _cache ??= await FetchAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<IReadOnlyList<FieldDefinition>> FetchAsync(CancellationToken cancellationToken)
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
                var fields = new List<FieldDefinition>();
                if (document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in values.EnumerateArray())
                    {
                        var referenceName = GetString(field, "referenceName");
                        if (string.IsNullOrEmpty(referenceName))
                        {
                            continue;
                        }

                        fields.Add(new FieldDefinition(referenceName!, GetString(field, "name") ?? referenceName!, GetString(field, "type")));
                    }
                }

                return fields;
            }
        }

        private static string? GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
