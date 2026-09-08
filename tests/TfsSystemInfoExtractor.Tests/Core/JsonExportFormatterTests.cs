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
        public void Serializes_extra_field_values_and_the_available_field_catalogue()
        {
            var node = ResultBuilder.Node(1);
            node.Fields = new System.Collections.Generic.Dictionary<string, string> { ["System.AssignedTo"] = "Jane Doe" };
            var result = new TfsSystemInfoExtractor.Core.Model.ExtractionResult(
                new System.DateTimeOffset(2026, 9, 8, 12, 0, 0, System.TimeSpan.Zero),
                new[] { node }, 1, 0, 0,
                new[] { new TfsSystemInfoExtractor.Core.Model.FieldDefinition("System.AssignedTo", "Assigned To") });

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(_formatter.Render(result).Content));

            Assert.Equal("Jane Doe", doc.RootElement.GetProperty("Roots")[0].GetProperty("Fields").GetProperty("System.AssignedTo").GetString());
            var field = doc.RootElement.GetProperty("AvailableFields")[0];
            Assert.Equal("System.AssignedTo", field.GetProperty("ReferenceName").GetString());
            Assert.Equal("Assigned To", field.GetProperty("DisplayName").GetString());
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
