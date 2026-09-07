using System.Text;
using System.Text.Json;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class JsonExportFormatterTests
    {
        private readonly JsonExportFormatter _formatter = new JsonExportFormatter();

        [Fact]
        public void Produces_the_pascal_case_shape_the_browser_consumes()
        {
            var result = ResultBuilder.Result(
                ResultBuilder.Node(1, type: "Feature", title: "Root", systemInfo: "info").WithChildren(
                    ResultBuilder.Node(2, error: "nope")));

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(_formatter.Render(result).Content));
            var root = doc.RootElement;

            Assert.Equal("2026-09-07T12:00:00", root.GetProperty("GeneratedAt").GetString());
            var first = root.GetProperty("Roots")[0];
            Assert.Equal(1, first.GetProperty("Id").GetInt32());
            Assert.Equal("Feature", first.GetProperty("Type").GetString());
            Assert.Equal("info", first.GetProperty("SystemInfo").GetString());
            Assert.False(first.TryGetProperty("Error", out _));          // null Error omitted
            Assert.False(first.TryGetProperty("HasSystemInfo", out _));  // computed prop not serialized

            var child = first.GetProperty("Children")[0];
            Assert.Equal("nope", child.GetProperty("Error").GetString());
        }

        [Fact]
        public void Does_not_escape_non_ascii()
        {
            var result = ResultBuilder.Result(ResultBuilder.Node(1, systemInfo: "שלום"));
            var json = Encoding.UTF8.GetString(_formatter.Render(result).Content);
            Assert.Contains("שלום", json);
        }
    }
}
