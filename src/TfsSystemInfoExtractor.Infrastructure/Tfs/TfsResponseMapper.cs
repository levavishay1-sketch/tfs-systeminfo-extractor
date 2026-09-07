using System.Collections.Generic;
using System.Text.Json;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>Pure mapping from a TFS work-item JSON payload to <see cref="RawWorkItem"/>.</summary>
    internal static class TfsResponseMapper
    {
        public static RawWorkItem ToRawWorkItem(int id, JsonElement root, string systemInfoFieldRef, TfsOptions options)
        {
            var fields = root.TryGetProperty("fields", out var f) ? f : default;

            return new RawWorkItem(
                id: id,
                type: GetFieldString(fields, "System.WorkItemType"),
                title: GetFieldString(fields, "System.Title"),
                state: GetFieldString(fields, "System.State"),
                webUrl: GetHtmlLink(root),
                systemInfoHtml: GetFieldString(fields, systemInfoFieldRef),
                childIds: GetChildIds(root, options.ChildLinkRelation));
        }

        private static string? GetFieldString(JsonElement fields, string referenceName)
        {
            if (fields.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(referenceName))
            {
                return null;
            }

            if (!fields.TryGetProperty(referenceName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static string? GetHtmlLink(JsonElement root)
        {
            if (root.TryGetProperty("_links", out var links) &&
                links.TryGetProperty("html", out var html) &&
                html.TryGetProperty("href", out var href) &&
                href.ValueKind == JsonValueKind.String)
            {
                return href.GetString();
            }

            return null;
        }

        private static IReadOnlyList<int> GetChildIds(JsonElement root, string childRelation)
        {
            var result = new List<int>();
            if (!root.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var relation in relations.EnumerateArray())
            {
                if (!relation.TryGetProperty("rel", out var rel) || rel.GetString() != childRelation)
                {
                    continue;
                }

                if (!relation.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = url.GetString();
                var lastSlash = text!.LastIndexOf('/');
                if (lastSlash >= 0 && int.TryParse(text.Substring(lastSlash + 1), out var childId))
                {
                    result.Add(childId);
                }
            }

            return result;
        }
    }
}
