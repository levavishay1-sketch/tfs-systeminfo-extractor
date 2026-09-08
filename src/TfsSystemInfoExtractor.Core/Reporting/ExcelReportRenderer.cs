using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>
    /// Renders the prepared <see cref="ReportView"/> as a formatted <c>.xlsx</c> workbook:
    /// the same rows, column order, selected fields, filter, hierarchy and ordering the UI
    /// (and the PDF export) are showing - this renderer only formats that state, it never
    /// re-decides what to show.
    /// <para>
    /// The sheet is a real Excel table (banded rows + column auto-filter), the header row
    /// is frozen and styled, parent Work Items are bold on a tint while children are
    /// indented in the Type column, long text wraps and is never truncated, and columns
    /// are sized to their content. Built on <c>DocumentFormat.OpenXml</c> only (MIT, no
    /// commercial dependency).
    /// </para>
    /// </summary>
    public sealed class ExcelReportRenderer : IReportRenderer
    {
        private const string ContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        // rows 1..3 are the title block, row 4 is the table header, data starts at row 5.
        private const int TitleRow = 1;
        private const int MetaRow = 2;
        private const int HeaderRow = 4;
        private const int FirstDataRow = 5;

        public ExportFormat Format => ExportFormat.Excel;

        public ExportArtifact Render(ReportView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            view.Validate();

            using var buffer = new MemoryStream();
            using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
                var styleCatalog = new StyleCatalog();
                styles.Stylesheet = styleCatalog.Stylesheet;

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var columns = view.Columns;
                var maxDepth = view.Rows.Count == 0 ? 0 : view.Rows.Max(r => Math.Max(0, r.Depth));
                var lastColumnRef = ColumnLetter(columns.Count);
                var lastDataRow = FirstDataRow + Math.Max(view.Rows.Count, 1) - 1;

                var sheetData = new SheetData();
                sheetData.Append(TitleBlock(view, styleCatalog));
                sheetData.Append(HeaderRowElement(columns, styleCatalog));
                foreach (var (row, index) in view.Rows.Select((r, i) => (r, i)))
                {
                    sheetData.Append(DataRowElement(row, columns, styleCatalog, FirstDataRow + index));
                }

                if (view.Rows.Count == 0)
                {
                    // keep the table range valid even with no results
                    sheetData.Append(EmptyRowElement(columns, styleCatalog, FirstDataRow));
                }

                var worksheet = new Worksheet();
                worksheet.Append(FrozenHeaderView());
                worksheet.Append(new SheetFormatProperties { DefaultRowHeight = 15D });
                worksheet.Append(BuildColumns(view, maxDepth));
                worksheet.Append(sheetData);
                worksheetPart.Worksheet = worksheet;

                var tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
                tablePart.Table = BuildTable(columns, lastColumnRef, lastDataRow);
                worksheet.Append(new TableParts(new TablePart { Id = worksheetPart.GetIdOfPart(tablePart) })
                {
                    Count = 1U
                });

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "System Info"
                });

                workbookPart.Workbook.Save();
            }

            return new ExportArtifact(
                ExportFormat.Excel,
                ExportFileNaming.ForTimestamp("xlsx"),
                ContentType,
                buffer.ToArray());
        }

        private static IEnumerable<OpenXmlElement> TitleBlock(ReportView view, StyleCatalog styles)
        {
            yield return new Row(TextCell("A" + TitleRow, view.Title, styles.Title)) { RowIndex = (uint)TitleRow };

            var meta = string.IsNullOrEmpty(view.GeneratedAt) ? string.Empty : "Generated " + view.GeneratedAt;
            if (!string.IsNullOrEmpty(view.Subtitle))
            {
                meta = meta.Length == 0 ? view.Subtitle! : meta + "  •  " + view.Subtitle;
            }

            yield return new Row(TextCell("A" + MetaRow, meta, styles.Meta)) { RowIndex = (uint)MetaRow };
            yield return new Row { RowIndex = 3U };
        }

        private static Row HeaderRowElement(IReadOnlyList<ReportColumn> columns, StyleCatalog styles)
        {
            var row = new Row { RowIndex = (uint)HeaderRow };
            for (var i = 0; i < columns.Count; i++)
            {
                row.Append(TextCell(ColumnLetter(i + 1) + HeaderRow, columns[i].Label, styles.Header));
            }

            return row;
        }

        private static Row DataRowElement(ReportRow row, IReadOnlyList<ReportColumn> columns, StyleCatalog styles, int rowIndex)
        {
            var isParent = row.Depth <= 0;
            var element = new Row { RowIndex = (uint)rowIndex };

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var text = row.Cells.TryGetValue(column.Key, out var value) ? value ?? string.Empty : string.Empty;
                var indent = column.Kind == ReportColumnKind.Type && row.Depth > 0 ? row.Depth : 0;
                var style = styles.Body(isParent, row.Error, indent);
                element.Append(TextCell(ColumnLetter(i + 1) + rowIndex, text, style));
            }

            return element;
        }

        private static Row EmptyRowElement(IReadOnlyList<ReportColumn> columns, StyleCatalog styles, int rowIndex)
        {
            var element = new Row { RowIndex = (uint)rowIndex };
            for (var i = 0; i < columns.Count; i++)
            {
                var text = i == 0 ? "No Work Items to show." : string.Empty;
                element.Append(TextCell(ColumnLetter(i + 1) + rowIndex, text, styles.Body(false, false, 0)));
            }

            return element;
        }

        private static Cell TextCell(string reference, string text, uint styleIndex) => new Cell
        {
            CellReference = reference,
            StyleIndex = styleIndex,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve })
        };

        private static SheetViews FrozenHeaderView()
        {
            var pane = new Pane
            {
                VerticalSplit = HeaderRow,
                TopLeftCell = "A" + FirstDataRow,
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            };

            var view = new SheetView { WorkbookViewId = 0U };
            view.Append(pane);
            view.Append(new Selection { Pane = PaneValues.BottomLeft });
            return new SheetViews(view);
        }

        private static Columns BuildColumns(ReportView view, int maxDepth)
        {
            var columns = new Columns();
            for (var i = 0; i < view.Columns.Count; i++)
            {
                var column = view.Columns[i];
                var longest = view.Rows
                    .Select(r => r.Cells.TryGetValue(column.Key, out var v) ? (v ?? string.Empty) : string.Empty)
                    .Select(LongestLine)
                    .DefaultIfEmpty(0)
                    .Max();
                longest = Math.Max(longest, column.Label.Length);

                double width;
                switch (column.Kind)
                {
                    case ReportColumnKind.Id:
                        width = Clamp(longest + 2, 8, 14);
                        break;
                    case ReportColumnKind.Type:
                        width = Clamp(longest + 4 + maxDepth * 2, 16, 40);
                        break;
                    case ReportColumnKind.Multiline:
                        width = 60;
                        break;
                    default:
                        width = Clamp(longest + 2, 12, 45);
                        break;
                }

                columns.Append(new Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = width,
                    CustomWidth = true
                });
            }

            return columns;
        }

        private static Table BuildTable(IReadOnlyList<ReportColumn> columns, string lastColumnRef, int lastDataRow)
        {
            var range = "A" + HeaderRow + ":" + lastColumnRef + lastDataRow;

            var tableColumns = new TableColumns { Count = (uint)columns.Count };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i].Label;
                if (string.IsNullOrWhiteSpace(name)) name = "Column " + (i + 1);
                var candidate = name;
                var suffix = 2;
                while (!used.Add(candidate))
                {
                    candidate = name + " " + suffix++;
                }

                tableColumns.Append(new TableColumn { Id = (uint)(i + 1), Name = candidate });
            }

            var table = new Table
            {
                Id = 1U,
                Name = "SystemInfo",
                DisplayName = "SystemInfo",
                Reference = range,
                TotalsRowShown = false
            };
            table.Append(new AutoFilter { Reference = range });
            table.Append(tableColumns);
            table.Append(new TableStyleInfo
            {
                Name = "TableStyleMedium2",
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = true,
                ShowColumnStripes = false
            });

            return table;
        }

        private static int LongestLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return value.Replace("\r\n", "\n").Split('\n').Max(line => line.Length);
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        private static string ColumnLetter(int oneBased)
        {
            var result = string.Empty;
            while (oneBased > 0)
            {
                var remainder = (oneBased - 1) % 26;
                result = (char)('A' + remainder) + result;
                oneBased = (oneBased - 1) / 26;
            }

            return result;
        }

        /// <summary>
        /// Owns the workbook stylesheet and hands out cell-format indices. The fixed
        /// fonts / fills / borders are declared up front; the indent-aware body formats
        /// are created on demand and cached, so the sheet only carries the styles it uses.
        /// </summary>
        private sealed class StyleCatalog
        {
            private readonly CellFormats _cellFormats;
            private readonly Dictionary<string, uint> _bodyCache = new Dictionary<string, uint>();

            public StyleCatalog()
            {
                var fonts = new Fonts(
                    new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),                                   // 0 normal
                    new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),                       // 1 bold
                    new Font(new Bold(), new FontSize { Val = 16D }, new FontName { Val = "Calibri" }),                       // 2 title
                    new Font(new FontSize { Val = 10D }, new Color { Rgb = "FF7F7F7F" }, new FontName { Val = "Calibri" }),   // 3 muted
                    new Font(new FontSize { Val = 11D }, new Color { Rgb = "FF9C1C1C" }, new FontName { Val = "Calibri" }),   // 4 error
                    new Font(new Bold(), new FontSize { Val = 11D }, new Color { Rgb = "FF9C1C1C" }, new FontName { Val = "Calibri" }), // 5 error+bold
                    new Font(new Bold(), new FontSize { Val = 11D }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" })  // 6 header
                )
                { Count = 7U };

                var fills = new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),                                          // 0 (reserved)
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),                                        // 1 (reserved)
                    SolidFill("FF1F3864"),                                                                                   // 2 header
                    SolidFill("FFD9E2F3"),                                                                                   // 3 parent tint
                    SolidFill("FFF8D7DA")                                                                                    // 4 error tint
                )
                { Count = 5U };

                var borders = new Borders(
                    new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()), // 0 none
                    ThinBorder()                                                                                             // 1 thin grid
                )
                { Count = 2U };

                _cellFormats = new CellFormats(
                    new CellFormat(),                                                                                       // 0 default
                    new CellFormat { FontId = 2U, ApplyFont = true },                                                        // 1 title
                    new CellFormat { FontId = 3U, ApplyFont = true },                                                        // 2 meta
                    new CellFormat                                                                                           // 3 header
                    {
                        FontId = 6U,
                        FillId = 2U,
                        BorderId = 1U,
                        ApplyFont = true,
                        ApplyFill = true,
                        ApplyBorder = true,
                        Alignment = new Alignment
                        {
                            Horizontal = HorizontalAlignmentValues.Left,
                            Vertical = VerticalAlignmentValues.Center,
                            WrapText = true
                        }
                    });
                _cellFormats.Count = 4U;

                Stylesheet = new Stylesheet(fonts, fills, borders,
                    new CellStyleFormats(new CellFormat()), _cellFormats,
                    new CellStyles(new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U }));
            }

            public Stylesheet Stylesheet { get; }

            public uint Title => 1U;

            public uint Meta => 2U;

            public uint Header => 3U;

            public uint Body(bool parent, bool error, int indent)
            {
                var key = (parent ? "p" : "-") + (error ? "e" : "-") + indent;
                if (_bodyCache.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                var fontId = error ? (parent ? 5U : 4U) : (parent ? 1U : 0U);
                var fillId = error ? 4U : (parent ? 3U : 0U);
                var applyFill = error || parent;

                var format = new CellFormat
                {
                    FontId = fontId,
                    FillId = fillId,
                    BorderId = 1U,
                    ApplyFont = true,
                    ApplyBorder = true,
                    ApplyFill = applyFill,
                    ApplyAlignment = true,
                    Alignment = new Alignment
                    {
                        Vertical = VerticalAlignmentValues.Top,
                        WrapText = true,
                        Indent = indent > 0 ? (uint)indent : 0U
                    }
                };

                _cellFormats.Append(format);
                var index = _cellFormats.Count!.Value;
                _cellFormats.Count = index + 1U;
                _bodyCache[key] = index;
                return index;
            }

            private static Fill SolidFill(string argb) => new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = argb },
                BackgroundColor = new BackgroundColor { Indexed = 64U }
            });

            private static Border ThinBorder() => new Border(
                new LeftBorder(new Color { Rgb = "FFD0D0D0" }) { Style = BorderStyleValues.Thin },
                new RightBorder(new Color { Rgb = "FFD0D0D0" }) { Style = BorderStyleValues.Thin },
                new TopBorder(new Color { Rgb = "FFD0D0D0" }) { Style = BorderStyleValues.Thin },
                new BottomBorder(new Color { Rgb = "FFD0D0D0" }) { Style = BorderStyleValues.Thin },
                new DiagonalBorder());
        }
    }
}
