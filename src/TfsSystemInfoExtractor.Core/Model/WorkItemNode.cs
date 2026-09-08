using System.Collections.Generic;
using System.Text.Json.Serialization;
using TfsSystemInfoExtractor.Core.Model.SourceControl;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>
    /// A single work item in the extracted hierarchy tree, with its System Info
    /// already converted to plain text. <see cref="Error"/> is set instead of the
    /// other fields when the item could not be loaded from TFS.
    /// </summary>
    public sealed class WorkItemNode
    {
        public int Id { get; set; }

        public string? Type { get; set; }

        public string? Title { get; set; }

        public string? State { get; set; }

        public string? Url { get; set; }

        public string? SystemInfo { get; set; }

        /// <summary>Populated when this item failed to load; the other fields are then meaningless.</summary>
        public string? Error { get; set; }

        /// <summary>
        /// Source-control activity linked to this work item (repositories, commits,
        /// contributors). Null until a source-control provider populates it.
        /// </summary>
        public SourceControlInfo? SourceControl { get; set; }

        /// <summary>
        /// Additional TFS field values, keyed by reference name (e.g.
        /// <c>System.AssignedTo</c>). Excludes the fields already surfaced as
        /// first-class properties (Id, Type, Title, State) and the System Info field.
        /// Null when the item carries no extra fields.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Fields { get; set; }

        public List<WorkItemNode> Children { get; } = new List<WorkItemNode>();

        [JsonIgnore]
        public bool HasSystemInfo => Error == null && !string.IsNullOrEmpty(SystemInfo);
    }
}
