using System.Text;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class CsvExportFormatterTests
    {
        private readonly CsvExportFormatter _formatter = new CsvExportFormatter();

        private const char Bom = (char)0xFEFF;

        private static string Text(ExportArtifact artifact)
        {
            return Encoding.UTF8.GetString(artifact.Content).TrimStart(Bom);
        }

        [Fact]
        public void Emits_header_then_one_row_per_node_in_tree_order()
        {
            var result = ResultBuilder.Result(
                ResultBuilder.Node(1, systemInfo: "info-1").WithChildren(
                    ResultBuilder.Node(2),
                    ResultBuilder.Node(3, error: "boom")));

            var lines = Text(_formatter.Render(result)).Split(new[] { "\r\n" }, System.StringSplitOptions.None);

            Assert.Equal("ID,Type,Title,State,Info,System Info", lines[0]);
            Assert.StartsWith("1,Task,Item 1,Active,Yes,info-1", lines[1]);
            Assert.StartsWith("2,Task,Item 2,Active,No,", lines[2]);
            Assert.StartsWith("3,Task,Item 3,Active,ERROR,boom", lines[3]);
        }

        [Fact]
        public void Quotes_fields_containing_comma_quote_or_newline()
        {
            var result = ResultBuilder.Result(
                ResultBuilder.Node(1, title: "a, b", systemInfo: "line1\nsays \"hi\""));

            var row = Text(_formatter.Render(result)).Split(new[] { "\r\n" }, System.StringSplitOptions.None)[1];

            Assert.Contains("\"a, b\"", row);
            Assert.Contains("\"line1\nsays \"\"hi\"\"\"", row);
        }

        [Fact]
        public void Includes_a_utf8_bom_for_excel()
        {
            var bytes = _formatter.Render(ResultBuilder.Result(ResultBuilder.Node(1))).Content;
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });
        }

        [Fact]
        public void Filename_uses_csv_extension_and_generated_timestamp()
        {
            var artifact = _formatter.Render(ResultBuilder.Result(ResultBuilder.Node(1)));
            Assert.Equal("TfsSystemInfo_2026-09-07_120000.csv", artifact.FileName);
            Assert.Equal(ExportFormat.Csv, artifact.Format);
        }
    }
}
