using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Infrastructure.Text;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class HierarchyWalkerTests
    {
        private static HierarchyWalker Build(FakeWorkItemSource source, int maxDepth = 50) =>
            new HierarchyWalker(
                source,
                new HtmlToPlainTextConverter(),
                Options.Create(new ExtractionOptions { MaxDepth = maxDepth }));

        private static Task<System.Collections.Generic.IReadOnlyList<WorkItemNode>> Walk(HierarchyWalker walker, params int[] roots) =>
            walker.WalkAsync(roots, NullProgressListener.Instance, CancellationToken.None);

        [Fact]
        public async Task Shared_child_is_expanded_only_once()
        {
            var source = new FakeWorkItemSource()
                .Add(1, childIds: new[] { 2, 3 })
                .Add(2, childIds: new[] { 4 })
                .Add(3, childIds: new[] { 4 })
                .Add(4);

            var roots = await Walk(Build(source), 1);

            var one = Assert.Single(roots);
            var two = one.Children.Single(c => c.Id == 2);
            var three = one.Children.Single(c => c.Id == 3);
            Assert.Single(two.Children);              // 4 lives under the first parent
            Assert.Empty(three.Children);             // not repeated under the second
            Assert.Equal(1, source.FetchCounts[4]);   // and fetched once
        }

        [Fact]
        public async Task Unreadable_item_becomes_an_error_node_without_aborting()
        {
            var source = new FakeWorkItemSource()
                .Add(1, childIds: new[] { 2, 999 })
                .Add(2);

            var roots = await Walk(Build(source), 1);

            var children = roots.Single().Children;
            Assert.Equal(2, children.Count);
            Assert.Null(children[0].Error);
            Assert.NotNull(children[1].Error);
            Assert.Contains("999", children[1].Error);
        }

        [Fact]
        public async Task Child_order_is_preserved()
        {
            var source = new FakeWorkItemSource()
                .Add(1, childIds: new[] { 30, 10, 20 })
                .Add(10).Add(20).Add(30);

            var roots = await Walk(Build(source), 1);

            Assert.Equal(new[] { 30, 10, 20 }, roots.Single().Children.Select(c => c.Id));
        }

        [Fact]
        public async Task System_info_html_is_converted_to_plain_text()
        {
            var source = new FakeWorkItemSource().Add(1, "<p>Browser: Chrome</p><div>OS: Windows</div>");

            var roots = await Walk(Build(source), 1);

            Assert.Equal("Browser: Chrome\nOS: Windows", roots.Single().SystemInfo);
            Assert.True(roots.Single().HasSystemInfo);
        }

        [Fact]
        public async Task Max_depth_is_enforced()
        {
            var source = new FakeWorkItemSource()
                .Add(1, childIds: new[] { 2 })
                .Add(2, childIds: new[] { 3 })
                .Add(3, childIds: new[] { 4 })
                .Add(4);

            var roots = await Walk(Build(source, maxDepth: 2), 1);

            var deepest = roots.Single().Children.Single().Children.Single();
            Assert.Equal(3, deepest.Id);
            Assert.NotNull(deepest.Error);
            Assert.Contains("depth", deepest.Error);
            Assert.False(source.FetchCounts.ContainsKey(4));
        }
    }
}
