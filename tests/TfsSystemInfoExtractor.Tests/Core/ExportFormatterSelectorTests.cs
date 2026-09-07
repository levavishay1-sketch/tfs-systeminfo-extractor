using System.Linq;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class ExportFormatterSelectorTests
    {
        private readonly ExportFormatterSelector _selector = new ExportFormatterSelector(new IExportFormatter[]
        {
            new JsonExportFormatter(),
            new MarkdownExportFormatter(),
            new CsvExportFormatter()
        });

        [Theory]
        [InlineData("json", ExportFormat.Json)]
        [InlineData("JSON", ExportFormat.Json)]
        [InlineData("md", ExportFormat.Markdown)]
        [InlineData("markdown", ExportFormat.Markdown)]
        [InlineData("csv", ExportFormat.Csv)]
        public void Resolves_known_tokens(string token, ExportFormat expected)
        {
            Assert.True(_selector.TryResolve(token, out var formatter));
            Assert.Equal(expected, formatter.Format);
        }

        [Theory]
        [InlineData("xml")]
        [InlineData("")]
        [InlineData(null)]
        public void Rejects_unknown_tokens(string? token)
        {
            Assert.False(_selector.TryResolve(token, out _));
        }

        [Fact]
        public void Exposes_all_registered_formatters()
        {
            Assert.Equal(3, _selector.All.Count());
        }
    }
}
