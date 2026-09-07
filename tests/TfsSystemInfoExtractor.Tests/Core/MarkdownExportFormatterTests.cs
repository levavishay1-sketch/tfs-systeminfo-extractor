using System;
using System.Text;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class MarkdownExportFormatterTests
    {
        private readonly MarkdownExportFormatter _formatter = new MarkdownExportFormatter();

        private string Render(params WorkItemNode[] roots) =>
            Encoding.UTF8.GetString(_formatter.Render(ResultBuilder.Result(roots)).Content);

        [Fact]
        public void Nests_headings_by_depth_and_labels_roots_vs_children()
        {
            var md = Render(ResultBuilder.Node(1, systemInfo: "x").WithChildren(ResultBuilder.Node(2, systemInfo: "y")));

            Assert.Contains("# Work Item 1", md);
            Assert.Contains("## Child Work Item 2", md);
        }

        [Fact]
        public void Renders_error_items_distinctly_and_stops_there()
        {
            var md = Render(ResultBuilder.Node(9, error: "does not exist (404)"));

            Assert.Contains("**ERROR loading this item:** does not exist (404)", md);
            Assert.DoesNotContain("System Info", md.Substring(md.IndexOf("Work Item 9", StringComparison.Ordinal)));
        }

        [Fact]
        public void Marks_empty_system_info()
        {
            Assert.Contains("_(empty)_", Render(ResultBuilder.Node(1, systemInfo: null)));
        }
    }
}
