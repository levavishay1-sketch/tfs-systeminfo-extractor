using System;
using System.Collections.Generic;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>
    /// The presentation state the UI has already prepared for export: the columns the
    /// user chose, the rows that survive the current filters in the current hierarchy /
    /// order, plus the exact rendered table HTML. It carries no domain logic - the
    /// browser has already decided <em>what</em> is shown; renderers only format it.
    /// </summary>
    public sealed class ReportView
    {
        public string Title { get; set; } = "Report";

        /// <summary>Optional line under the title, e.g. an active-filter note.</summary>
        public string? Subtitle { get; set; }

        /// <summary>Free-text timestamp as the UI displays it (already formatted).</summary>
        public string? GeneratedAt { get; set; }

        public List<ReportColumn> Columns { get; set; } = new List<ReportColumn>();

        public List<ReportRow> Rows { get; set; } = new List<ReportRow>();

        /// <summary>
        /// The exact table markup the user is looking at (the <c>#viewport</c> inner HTML).
        /// HTML-based renderers use this verbatim; text renderers use <see cref="Columns"/>
        /// and <see cref="Rows"/>.
        /// </summary>
        public string? Html { get; set; }

        public void Validate()
        {
            if (Columns.Count == 0)
            {
                throw new ArgumentException("A report view needs at least one column.");
            }
        }
    }

    public sealed class ReportColumn
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public ReportColumnKind Kind { get; set; } = ReportColumnKind.Text;
    }

    public enum ReportColumnKind
    {
        Id,
        Type,
        Text,
        Multiline
    }

    public sealed class ReportRow
    {
        /// <summary>Hierarchy depth of this row in the currently displayed tree (0 = a group root).</summary>
        public int Depth { get; set; }

        /// <summary>True when this row starts a new visual group (a top-level Work Item in the current view).</summary>
        public bool GroupStart { get; set; }

        public bool Error { get; set; }

        /// <summary>Cell text keyed by <see cref="ReportColumn.Key"/>.</summary>
        public Dictionary<string, string> Cells { get; set; } = new Dictionary<string, string>();
    }
}
