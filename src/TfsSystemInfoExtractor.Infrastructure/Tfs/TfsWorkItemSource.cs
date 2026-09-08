using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// TFS-backed <see cref="IWorkItemSource"/>. Fetches one work item expanded with
    /// its relations and maps transport failures to <see cref="WorkItemAccessException"/>
    /// so a single unreadable item never aborts the whole extraction.
    /// </summary>
    public sealed class TfsWorkItemSource : IWorkItemSource
    {
        private readonly TfsRestClient _client;
        private readonly ISystemInfoFieldResolver _fieldResolver;
        private readonly IFieldCatalog _fieldCatalog;
        private readonly IHtmlToText _htmlToText;
        private readonly TfsOptions _options;

        public TfsWorkItemSource(
            TfsRestClient client,
            ISystemInfoFieldResolver fieldResolver,
            IFieldCatalog fieldCatalog,
            IHtmlToText htmlToText,
            IOptions<TfsOptions> options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _fieldResolver = fieldResolver ?? throw new ArgumentNullException(nameof(fieldResolver));
            _fieldCatalog = fieldCatalog ?? throw new ArgumentNullException(nameof(fieldCatalog));
            _htmlToText = htmlToText ?? throw new ArgumentNullException(nameof(htmlToText));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        }

        public async Task<RawWorkItem> GetAsync(int id, CancellationToken cancellationToken)
        {
            var fieldRef = await _fieldResolver.ResolveReferenceNameAsync(cancellationToken).ConfigureAwait(false);
            var catalog = (await _fieldCatalog.GetFieldsAsync(cancellationToken).ConfigureAwait(false))
                .GroupBy(f => f.ReferenceName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            try
            {
                using var document = await _client
                    .GetJsonAsync($"_apis/wit/workitems/{id.ToString(CultureInfo.InvariantCulture)}", "$expand=relations", cancellationToken)
                    .ConfigureAwait(false);

                return TfsResponseMapper.ToRawWorkItem(id, document.RootElement, fieldRef, catalog, _htmlToText, _options);
            }
            catch (TfsUnreachableException ex)
            {
                throw new WorkItemAccessException(id, WorkItemAccessError.Network, $"Network error loading work item {id}: {ex.Message}", ex);
            }
            catch (TfsHttpException ex)
            {
                throw ToAccessException(id, ex);
            }
        }

        private static WorkItemAccessException ToAccessException(int id, TfsHttpException ex)
        {
            switch (ex.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new WorkItemAccessException(id, WorkItemAccessError.NotFound, $"Work item {id} does not exist (404).", ex);
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new WorkItemAccessException(id, WorkItemAccessError.Forbidden, $"No permission to read work item {id} ({(int)ex.StatusCode}).", ex);
                default:
                    var error = (int)ex.StatusCode >= 500 ? WorkItemAccessError.Server : WorkItemAccessError.Protocol;
                    return new WorkItemAccessException(id, error, ex.Message, ex);
            }
        }
    }
}
