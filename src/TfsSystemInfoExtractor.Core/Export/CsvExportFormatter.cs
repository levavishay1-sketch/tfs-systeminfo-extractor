using System.Text;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Export
{
    /// <summary>
    /// Renders the hierarchy as a flat CSV in tree (depth-first) order, matching the
    /// columns shown by the Table view in the UI. RFC 4180 quoting; a UTF-8 BOM is
    /// emitted so Excel detects the encoding (important for Hebrew System Info).
    /// </summary>
    public sealed class CsvExportFormatter : IExportFormatter
    {
        private static readonly string[] Headers = { "ID", "Type", "Title", "State", "Info", "System Info" };

        public ExportFormat Format => ExportFormat.Csv;

        public ExportArtifact Render(ExtractionResult result)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", System.Array.ConvertAll(Headers, Escape)));

            foreach (var node in result.Roots.Flatten())
            {
                sb.Append("\r\n");
                sb.Append(string.Join(",", System.Array.ConvertAll(RowValues(node), Escape)));
            }

            return ExportArtifact.FromText(
                ExportFormat.Csv,
                ExportFileNaming.ForResult(result, "csv"),
                "text/csv; charset=utf-8",
                sb.ToString(),
                withBom: true);
        }

        private static string[] RowValues(WorkItemNode node) => new[]
        {
            node.Id.ToString(),
            node.Type ?? string.Empty,
            node.Title ?? string.Empty,
            node.State ?? string.Empty,
            node.Error != null ? "ERROR" : node.HasSystemInfo ? "Yes" : "No",
            node.Error ?? node.SystemInfo ?? string.Empty
        };

        private static string Escape(string value)
        {
            if (value.IndexOf('"') < 0 && value.IndexOf(',') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
