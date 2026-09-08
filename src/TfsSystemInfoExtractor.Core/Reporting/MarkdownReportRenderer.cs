using System.Linq;
using System.Text;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>Renders the prepared view as a Markdown table in the current column order.</summary>
    public sealed class MarkdownReportRenderer : IReportRenderer
    {
        public ExportFormat Format => ExportFormat.Markdown;

        public ExportArtifact Render(ReportView view)
        {
            view.Validate();

            var sb = new StringBuilder();
            sb.Append("# ").Append(view.Title).Append("\n\n");
            if (!string.IsNullOrEmpty(view.GeneratedAt))
            {
                sb.Append("Generated: ").Append(view.GeneratedAt).Append("\n\n");
            }

            if (!string.IsNullOrEmpty(view.Subtitle))
            {
                sb.Append("_").Append(view.Subtitle).Append("_\n\n");
            }

            sb.Append("| ").Append(string.Join(" | ", view.Columns.Select(c => Cell(c.Label)))).Append(" |\n");
            sb.Append('|').Append(string.Concat(view.Columns.Select(_ => " --- |"))).Append('\n');

            foreach (var row in view.Rows)
            {
                sb.Append("| ");
                sb.Append(string.Join(" | ", view.Columns.Select(column => Cell(CellText(row, column)))));
                sb.Append(" |\n");
            }

            return ExportArtifact.FromText(
                ExportFormat.Markdown,
                ExportFileNaming.ForTimestamp("md"),
                "text/markdown; charset=utf-8",
                sb.ToString());
        }

        private static string CellText(ReportRow row, ReportColumn column)
        {
            var text = row.Cells.TryGetValue(column.Key, out var value) ? value : string.Empty;
            if (column.Kind == ReportColumnKind.Type && row.Depth > 0)
            {
                text = string.Concat(Enumerable.Repeat("\u2007\u2007\u2007", row.Depth)) + "\u21b3 " + text;
            }

            return text;
        }

        private static string Cell(string value) =>
            value.Replace("\r\n", "\n").Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", "<br>");
    }
}
