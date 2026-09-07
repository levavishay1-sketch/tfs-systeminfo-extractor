using TfsSystemInfoExtractor.Infrastructure.Text;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Infrastructure
{
    public class HtmlToPlainTextConverterTests
    {
        private readonly HtmlToPlainTextConverter _converter = new HtmlToPlainTextConverter();

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void Blank_input_yields_empty(string? html, string expected)
        {
            Assert.Equal(expected, _converter.Convert(html));
        }

        [Fact]
        public void Line_breaks_and_block_tags_become_newlines()
        {
            var html = "<p>Browser: Chrome</p><div>OS: Windows</div>Env:<br/>PROD";
            Assert.Equal("Browser: Chrome\nOS: Windows\nEnv:\nPROD", _converter.Convert(html));
        }

        [Fact]
        public void Strips_remaining_tags_and_decodes_entities()
        {
            Assert.Equal("a > b & c", _converter.Convert("<span style='x'>a &gt; b &amp; c</span>"));
        }

        [Fact]
        public void Collapses_runs_of_blank_lines_and_trims_edges()
        {
            var html = "<p></p><p>one</p><p></p><p></p><p>two</p><p></p>";
            Assert.Equal("one\n\ntwo", _converter.Convert(html));
        }

        [Fact]
        public void Preserves_non_ascii_content()
        {
            Assert.Equal("שלום", _converter.Convert("<div>שלום</div>"));
        }
    }
}
