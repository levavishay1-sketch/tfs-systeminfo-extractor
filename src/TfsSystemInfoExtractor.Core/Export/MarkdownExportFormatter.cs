using System;
using System.Text;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Export
{
    /// <summary>Renders the result as a reading-oriented Markdown document, one section per work item.</summary>
    public sealed class MarkdownExportFormatter : IExportFormatter
    {
        public ExportFormat Format => ExportFormat.Markdown;

        public ExportArtifact Render(ExtractionResult result)
        {
            var sb = new StringBuilder();
            sb.Append("# TFS System Info Export\n\n");
            sb.Append($"Generated: {result.GeneratedAt:yyyy-MM-dd HH:mm:ss}\n\n");
            sb.Append($"Processed {result.ProcessedCount} work items - {result.WithSystemInfoCount} with System Info, {result.FailedCount} failed.\n\n");
            sb.Append("---\n\n");

            foreach (var root in result.Roots)
            {
                WriteNode(sb, root, depth: 1, isRoot: true);
            }

            return ExportArtifact.FromText(
                ExportFormat.Markdown,
                ExportFileNaming.ForResult(result, "md"),
                "text/markdown; charset=utf-8",
                sb.ToString());
        }

        private static void WriteNode(StringBuilder sb, WorkItemNode node, int depth, bool isRoot)
        {
            var header = new string('#', Math.Min(depth, 6));
            var label = isRoot ? "Work Item" : "Child Work Item";
            sb.Append($"{header} {label} {node.Id}\n\n");

            if (node.Error != null)
            {
                sb.Append($"**ERROR loading this item:** {node.Error}\n\n---\n\n");
                return;
            }

            sb.Append($"Type: {node.Type}\n\n");
            sb.Append($"Title: {node.Title}\n\n");
            sb.Append($"State: {node.State}\n\n");
            sb.Append($"URL: {node.Url}\n\n");

            var subHeader = new string('#', Math.Min(depth + 1, 6));
            sb.Append($"{subHeader} System Info\n\n");
            sb.Append(string.IsNullOrEmpty(node.SystemInfo) ? "_(empty)_\n\n" : node.SystemInfo + "\n\n");
            sb.Append("---\n\n");

            foreach (var child in node.Children)
            {
                WriteNode(sb, child, depth + 1, isRoot: false);
            }
        }
    }
}
