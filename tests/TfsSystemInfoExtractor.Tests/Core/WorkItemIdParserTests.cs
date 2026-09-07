using System.Linq;
using TfsSystemInfoExtractor.Core.Extraction;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class WorkItemIdParserTests
    {
        private readonly WorkItemIdParser _parser = new WorkItemIdParser();

        [Theory]
        [InlineData("46269, 44065, 42629")]
        [InlineData("46269\n44065\n42629")]
        [InlineData(" 46269 ; 44065\t42629 ")]
        public void Parses_ids_regardless_of_separator(string input)
        {
            Assert.Equal(new[] { 46269, 44065, 42629 }, _parser.Parse(input));
        }

        [Fact]
        public void Removes_duplicates_but_keeps_first_seen_order()
        {
            Assert.Equal(new[] { 3, 1, 2 }, _parser.Parse("3 1 2 1 3 2"));
        }

        [Fact]
        public void Ignores_non_numeric_and_non_positive_tokens()
        {
            Assert.Equal(new[] { 12 }, _parser.Parse("abc 12 -4 0 3.5"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Returns_empty_for_blank_input(string? input)
        {
            Assert.Empty(_parser.Parse(input));
        }
    }
}
