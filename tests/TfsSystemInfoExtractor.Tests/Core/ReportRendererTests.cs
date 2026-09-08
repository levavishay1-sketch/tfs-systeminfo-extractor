using System;
using System.Linq;
using System.Text;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class CsvReportRendererTests
    {
        private readonly CsvReportRenderer _renderer = new CsvReportRenderer();

        private static string Text(ExportArtifact a) =>
            Encoding.UTF8.GetString(a.Content).TrimStart((char)0xFEFF);

        [Fact]
        public void Emits_the_current_columns_in_order_then_one_line_per_row()
        {
            var lines = Text(_renderer.Render(ReportViewBuilder.Sample("Priority")))
                .Split(new[] { "\r\n" }, StringSplitOptions.None);

            Assert.Equal("ID,Type,Title,State,System Info,Priority", lines[0]);
            Assert.Equal(4, lines.Length); // header + 3 rows
            Assert.StartsWith("#46269,Feature,Onboarding portal,Active,", lines[1]);
        }

        [Fact]
        public void Child_rows_indent_the_type_cell_to_show_hierarchy()
        {
            var lines = Text(_renderer.Render(ReportViewBuilder.Sample()))
                .Split(new[] { "\r\n" }, StringSplitOptions.None);

            // depth-1 rows: Type cell prefixed
            Assert.Contains("  - User Story", lines[2]);
        }

        [Fact]
        public void Quotes_multiline_and_comma_and_preserves_hebrew_with_a_bom()
        {
            var bytes = _renderer.Render(ReportViewBuilder.Sample()).Content;
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

            var text = Text(_renderer.Render(ReportViewBuilder.Sample()));
            Assert.Contains("\"Browser: Chrome\nOS: Win11\"", text);
            Assert.Contains("כותרת בעברית", text);
        }

        [Fact]
        public void Filename_and_format()
        {
            var a = _renderer.Render(ReportViewBuilder.Sample());
            Assert.Equal(ExportFormat.Csv, a.Format);
            Assert.Matches(@"^TfsSystemInfo_\d{4}-\d{2}-\d{2}_\d{6}\.csv$", a.FileName);
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
            new CsvReportRenderer(),
            new MarkdownReportRenderer()
        });

        [Theory]
        [InlineData("csv", ExportFormat.Csv)]
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
