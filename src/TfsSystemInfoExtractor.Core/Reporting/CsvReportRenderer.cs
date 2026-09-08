using System.Linq;
using System.Text;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>
    /// Renders the prepared view as CSV in the current column order, one line per
    /// displayed row. Hierarchy is conveyed by indenting the Type cell. RFC 4180
    /// quoting; a UTF-8 BOM so Excel detects the encoding (matters for Hebrew).
    /// </summary>
    public sealed class CsvReportRenderer : IReportRenderer
    {
        public ExportFormat Format => ExportFormat.Csv;

        public ExportArtifact Render(ReportView view)
        {
            view.Validate();

            var sb = new StringBuilder();
            sb.Append(string.Join(",", view.Columns.Select(c => Escape(c.Label))));

            foreach (var row in view.Rows)
            {
                sb.Append("\r\n");
                sb.Append(string.Join(",", view.Columns.Select(column => Escape(CellText(row, column)))));
            }

            return ExportArtifact.FromText(
                ExportFormat.Csv,
                ExportFileNaming.ForTimestamp("csv"),
                "text/csv; charset=utf-8",
                sb.ToString(),
                withBom: true);
        }

        private static string CellText(ReportRow row, ReportColumn column)
        {
            var text = row.Cells.TryGetValue(column.Key, out var value) ? value : string.Empty;
            if (column.Kind == ReportColumnKind.Type && row.Depth > 0)
            {
                text = new string(' ', row.Depth * 2) + "- " + text;
            }

            return text;
        }

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
