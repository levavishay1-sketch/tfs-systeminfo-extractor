using System;
using System.Text.Json.Serialization;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>
    /// Metadata for one TFS work item field, as advertised by the collection's field
    /// catalogue. Used to offer the user a dynamic, non-hard-coded list of extra
    /// columns and to know which field values are HTML.
    /// </summary>
    public sealed class FieldDefinition
    {
        public FieldDefinition(string referenceName, string displayName, string? type = null)
        {
            if (string.IsNullOrWhiteSpace(referenceName))
            {
                throw new ArgumentException("Field reference name is required.", nameof(referenceName));
            }

            ReferenceName = referenceName;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? referenceName : displayName;
            Type = type;
        }

        /// <summary>Technical name, e.g. <c>System.AssignedTo</c>.</summary>
        public string ReferenceName { get; }

        /// <summary>Human-friendly name shown in the field picker and as the column header.</summary>
        public string DisplayName { get; }

        /// <summary>TFS field type, e.g. <c>string</c>, <c>html</c>, <c>dateTime</c>, <c>integer</c>, <c>treePath</c>.</summary>
        public string? Type { get; }

        [JsonIgnore]
        public bool IsHtml => string.Equals(Type, "html", StringComparison.OrdinalIgnoreCase);
    }
}
