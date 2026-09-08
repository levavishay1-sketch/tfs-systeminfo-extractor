using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Export
{
    /// <summary>
    /// Serializes an <see cref="ExtractionResult"/> to the JSON the browser consumes as
    /// its data feed (and offers as a raw "Download JSON"). Property names stay
    /// PascalCase and the shape (<c>GeneratedAt</c> / <c>Roots</c> / <c>AvailableFields</c>
    /// and node <c>Id,Type,Title,State,Url,SystemInfo,Error,Fields,SourceControl,Children</c>,
    /// where <c>SourceControl</c> is <c>{ Repositories, Commits, Contributors, Components }</c>)
    /// is a contract the UI relies on, so it must not drift.
    /// </summary>
    public sealed class JsonExportFormatter
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public ExportArtifact Render(ExtractionResult result)
        {
            var document = new JsonDocumentShape
            {
                GeneratedAt = result.GeneratedAt.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                Roots = result.Roots,
                AvailableFields = result.AvailableFields
            };

            var json = JsonSerializer.Serialize(document, Options);
            return ExportArtifact.FromText(
                ExportFormat.Json,
                ExportFileNaming.ForResult(result, "json"),
                "application/json; charset=utf-8",
                json);
        }

        private sealed class JsonDocumentShape
        {
            public string GeneratedAt { get; set; } = string.Empty;

            public System.Collections.Generic.IReadOnlyList<WorkItemNode> Roots { get; set; } =
                System.Array.Empty<WorkItemNode>();

            public System.Collections.Generic.IReadOnlyList<FieldDefinition> AvailableFields { get; set; } =
                System.Array.Empty<FieldDefinition>();
        }
    }
}
