using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class ExcelReportRendererTests
    {
        private readonly ExcelReportRenderer _renderer = new ExcelReportRenderer();

        private static (WorkbookPart Book, WorksheetPart Sheet) Open(ExportArtifact artifact)
        {
            var document = SpreadsheetDocument.Open(new MemoryStream(artifact.Content), false);
            var book = document.WorkbookPart!;
            var sheet = book.WorksheetParts.First();
            return (book, sheet);
        }

        private static string CellText(WorksheetPart sheet, string reference)
        {
            var cell = sheet.Worksheet.Descendants<Cell>().FirstOrDefault(c => c.CellReference == reference);
            if (cell?.InlineString != null) return cell.InlineString.InnerText;
            return cell?.CellValue?.InnerText ?? string.Empty;
        }

        [Fact]
        public void Is_a_valid_xlsx_with_the_right_format_and_name()
        {
            var artifact = _renderer.Render(ReportViewBuilder.Sample("Priority"));

            Assert.Equal(ExportFormat.Excel, artifact.Format);
            Assert.Matches(@"^TfsSystemInfo_\d{4}-\d{2}-\d{2}_\d{6}\.xlsx$", artifact.FileName);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", artifact.ContentType);
            Assert.Equal(new byte[] { 0x50, 0x4B }, artifact.Content.Take(2).ToArray()); // ZIP / OOXML magic

            var (_, sheet) = Open(artifact);
            Assert.NotNull(sheet.Worksheet);
        }

        [Fact]
        public void Header_row_carries_the_current_columns_in_order()
        {
            var (_, sheet) = Open(_renderer.Render(ReportViewBuilder.Sample("Priority")));

            Assert.Equal("ID", CellText(sheet, "A4"));
            Assert.Equal("Type", CellText(sheet, "B4"));
            Assert.Equal("Title", CellText(sheet, "C4"));
            Assert.Equal("State", CellText(sheet, "D4"));
            Assert.Equal("System Info", CellText(sheet, "E4"));
            Assert.Equal("Priority", CellText(sheet, "F4"));
        }

        [Fact]
        public void One_data_row_per_view_row_starting_below_the_frozen_header()
        {
            var (_, sheet) = Open(_renderer.Render(ReportViewBuilder.Sample()));

            Assert.Equal("#46269", CellText(sheet, "A5"));
            Assert.Equal("User Story", CellText(sheet, "B6"));
            Assert.Equal("(failed to load)", CellText(sheet, "C7"));

            var pane = sheet.Worksheet.Descendants<Pane>().Single();
            Assert.Equal(4D, pane.VerticalSplit!.Value);
            Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
        }

        [Fact]
        public void Child_rows_are_indented_in_the_type_column_to_show_hierarchy()
        {
            var (book, sheet) = Open(_renderer.Render(ReportViewBuilder.Sample()));
            var formats = book.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().ToList();

            uint IndentOf(string reference)
            {
                var cell = sheet.Worksheet.Descendants<Cell>().Single(c => c.CellReference == reference);
                var alignment = formats[(int)cell.StyleIndex!.Value].Alignment;
                return alignment?.Indent?.Value ?? 0U;
            }

            Assert.Equal(0U, IndentOf("B5")); // parent Feature
            Assert.Equal(1U, IndentOf("B6")); // depth-1 child
        }

        [Fact]
        public void Renders_a_real_excel_table_with_a_column_filter()
        {
            var (_, sheet) = Open(_renderer.Render(ReportViewBuilder.Sample("Priority")));
            var table = sheet.TableDefinitionParts.Single().Table;

            Assert.Equal("A4:F7", table.Reference!.Value);
            Assert.NotNull(table.AutoFilter);
            Assert.Equal(6, table.TableColumns!.Count());
            Assert.Equal("System Info", table.TableColumns!.Elements<TableColumn>().ElementAt(4).Name!.Value);
        }

        [Fact]
        public void Long_multiline_and_hebrew_values_are_kept_verbatim_and_set_to_wrap()
        {
            var (book, sheet) = Open(_renderer.Render(ReportViewBuilder.Sample()));
            var formats = book.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().ToList();

            Assert.Equal("Browser: Chrome\nOS: Win11", CellText(sheet, "E5"));
            Assert.Equal("כותרת בעברית — mixed", CellText(sheet, "C6"));

            var siCell = sheet.Worksheet.Descendants<Cell>().Single(c => c.CellReference == "E5");
            Assert.True(formats[(int)siCell.StyleIndex!.Value].Alignment!.WrapText!.Value);
        }
    }

    public class MarkdownReportRendererTests
    {
        private readonly MarkdownReportRenderer _renderer = new MarkdownReportRenderer();

        [Fact]
        public void Emits_a_markdown_table_with_the_current_columns_and_a_subtitle()
        {
            var view = ReportViewBuilder.Sample("Priority");
            view.Subtitle = "Filtered to Work Items that have System Info";

            var md = Encoding.UTF8.GetString(_renderer.Render(view).Content);

            Assert.Contains("| ID | Type | Title | State | System Info | Priority |", md);
            Assert.Contains("_Filtered to Work Items that have System Info_", md);
            Assert.Contains("<br>", md); // multiline System Info collapsed to <br>
            Assert.Contains("↳ User Story", md); // hierarchy cue on the Type cell
        }
    }

    public class ReportRendererSelectorTests
    {
        private readonly ReportRendererSelector _selector = new ReportRendererSelector(new IReportRenderer[]
        {
            new ExcelReportRenderer(),
            new MarkdownReportRenderer()
        });

        [Theory]
        [InlineData("xlsx", ExportFormat.Excel)]
        [InlineData("excel", ExportFormat.Excel)]
        [InlineData("EXCEL", ExportFormat.Excel)]
        [InlineData("md", ExportFormat.Markdown)]
        [InlineData("markdown", ExportFormat.Markdown)]
        [InlineData("MD", ExportFormat.Markdown)]
        public void Resolves_known_tokens(string token, ExportFormat expected)
        {
            Assert.True(_selector.TryResolve(token, out var renderer));
            Assert.Equal(expected, renderer.Format);
        }

        [Theory]
        [InlineData("pdf")]   // registered by the Web layer, absent here
        [InlineData("xml")]
        [InlineData("")]
        [InlineData(null)]
        public void Unresolvable_tokens_return_false(string? token)
        {
            Assert.False(_selector.TryResolve(token, out _));
        }
    }
}
