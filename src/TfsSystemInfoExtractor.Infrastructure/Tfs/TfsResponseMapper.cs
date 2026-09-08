using System;
using System.Collections.Generic;
using System.Text.Json;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>Pure mapping from a TFS work-item JSON payload to <see cref="RawWorkItem"/>.</summary>
    internal static class TfsResponseMapper
    {
        /// <summary>TFS relation type for a link to a commit / changeset / pull request / branch.</summary>
        private const string ArtifactLinkRelation = "ArtifactLink";
        /// <summary>Fields already surfaced as first-class <see cref="RawWorkItem"/> properties; never offered as "extra".</summary>
        private static readonly HashSet<string> DefaultFieldRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Id", "System.WorkItemType", "System.Title", "System.State"
        };

        public static RawWorkItem ToRawWorkItem(
            int id,
            JsonElement root,
            string systemInfoFieldRef,
            IReadOnlyDictionary<string, FieldDefinition> catalog,
            IHtmlToText htmlToText,
            TfsOptions options,
            SourceControlInfo? sourceControl = null)
        {
            var fields = root.TryGetProperty("fields", out var f) ? f : default;

            return new RawWorkItem(
                id: id,
                type: GetFieldString(fields, "System.WorkItemType"),
                title: GetFieldString(fields, "System.Title"),
                state: GetFieldString(fields, "System.State"),
                webUrl: GetHtmlLink(root),
                systemInfoHtml: GetFieldString(fields, systemInfoFieldRef),
                childIds: GetChildIds(root, options.ChildLinkRelation),
                sourceControl: sourceControl,
                fields: ExtractExtraFields(fields, systemInfoFieldRef, catalog, htmlToText));
        }

        /// <summary>
        /// The work item's <c>ArtifactLink</c> relations - links to commits, changesets,
        /// pull requests and branches. A source-control provider resolves the details.
        /// </summary>
        public static IReadOnlyList<WorkItemArtifactLink> ExtractArtifactLinks(JsonElement root)
        {
            var result = new List<WorkItemArtifactLink>();
            if (!root.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var relation in relations.EnumerateArray())
            {
                if (!relation.TryGetProperty("rel", out var rel) ||
                    !string.Equals(rel.GetString(), ArtifactLinkRelation, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!relation.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? name = null;
                if (relation.TryGetProperty("attributes", out var attributes) &&
                    attributes.ValueKind == JsonValueKind.Object &&
                    attributes.TryGetProperty("name", out var nameValue) &&
                    nameValue.ValueKind == JsonValueKind.String)
                {
                    name = nameValue.GetString();
                }

                result.Add(new WorkItemArtifactLink(url.GetString()!, name));
            }

            return result;
        }

        private static IReadOnlyDictionary<string, string> ExtractExtraFields(
            JsonElement fields,
            string systemInfoFieldRef,
            IReadOnlyDictionary<string, FieldDefinition> catalog,
            IHtmlToText htmlToText)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (fields.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in fields.EnumerateObject())
            {
                if (DefaultFieldRefs.Contains(property.Name) ||
                    string.Equals(property.Name, systemInfoFieldRef, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = FlattenValue(property.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (catalog.TryGetValue(property.Name, out var definition) && definition.IsHtml)
                {
                    value = htmlToText.Convert(value);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }
                }

                result[property.Name] = value;
            }

            return result;
        }

        private static string FlattenValue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    return value.GetRawText();
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Object:
                    // TFS identity fields (AssignedTo, ChangedBy, ...) are objects with a display name.
                    return value.TryGetProperty("displayName", out var displayName) && displayName.ValueKind == JsonValueKind.String
                        ? displayName.GetString() ?? string.Empty
                        : string.Empty;
                default:
                    return string.Empty;
            }
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
