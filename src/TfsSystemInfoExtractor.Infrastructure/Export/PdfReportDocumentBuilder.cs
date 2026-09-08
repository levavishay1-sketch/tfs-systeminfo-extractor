using System;
using System.Collections.Generic;
using System.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;

namespace TfsSystemInfoExtractor.Infrastructure.Export
{
    /// <summary>
    /// Builds the MigraDoc <see cref="Document"/> for the release report. This is pure
    /// layout: it takes an <see cref="ExtractionResult"/> and produces a document model,
    /// with no knowledge of PDF rendering. <see cref="PdfExportFormatter"/> turns the
    /// document into bytes.
    /// </summary>
    public sealed class PdfReportDocumentBuilder
    {
        private static readonly Color RootShading = new Color(0xFFEEF1F6);
        private static readonly Color HeaderShading = new Color(0xFF1F2937);
        private static readonly Color GroupRule = new Color(0xFF96A0AF);
        private static readonly Color Hairline = new Color(0xFFD6DAE2);
        private static readonly Color ErrorColor = new Color(0xFFB22222);
        private static readonly Color Muted = new Color(0xFF5A6473);

        public Document Build(ExtractionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var document = new Document();
            document.Info.Title = "TFS System Info - Release Report";
            document.Info.Author = "TFS System Info Extractor";
            ApplyStyles(document);

            var section = document.AddSection();
            var page = section.PageSetup;
            page.PageFormat = PageFormat.A4;
            page.Orientation = Orientation.Landscape;
            page.LeftMargin = page.RightMargin = Unit.FromCentimeter(1.5);
            page.TopMargin = Unit.FromCentimeter(1.4);
            page.BottomMargin = Unit.FromCentimeter(1.4);

            AddFooter(section);
            AddTitleBlock(section, result);

            if (result.Roots.Count == 0)
            {
                var empty = section.AddParagraph("No work items were extracted.");
                empty.Format.Font.Italic = true;
                empty.Format.Font.Color = Muted;
                return document;
            }

            AddHierarchyTable(section, result);
            return document;
        }

        private static void ApplyStyles(Document document)
        {
            var normal = document.Styles["Normal"];
            normal.Font.Name = "Segoe UI";
            normal.Font.Size = 8.5;
            normal.ParagraphFormat.SpaceAfter = 0;

            var title = document.Styles["Heading1"];
            title.Font.Name = "Segoe UI";
            title.Font.Size = 18;
            title.Font.Bold = true;
            title.ParagraphFormat.SpaceAfter = 4;

            var footer = document.Styles["Footer"];
            footer.Font.Size = 7.5;
            footer.Font.Color = Muted;
        }

        private static void AddFooter(Section section)
        {
            var footer = section.Footers.Primary.AddParagraph();
            footer.Style = "Footer";
            footer.AddText("TFS System Info Extractor  •  page ");
            footer.AddPageField();
            footer.AddText(" of ");
            footer.AddNumPagesField();
            footer.Format.Alignment = ParagraphAlignment.Right;
        }

        private static void AddTitleBlock(Section section, ExtractionResult result)
        {
            section.AddParagraph("TFS System Info – Release Report").Style = "Heading1";

            var meta = section.AddParagraph();
            meta.Format.Font.Color = Muted;
            meta.Format.SpaceAfter = 2;
            meta.AddText($"Generated {result.GeneratedAt:yyyy-MM-dd HH:mm}");

            var summary = section.AddParagraph();
            summary.Format.Font.Color = Muted;
            summary.Format.SpaceAfter = 10;
            summary.AddText(
                $"{result.ProcessedCount} work items  •  {result.WithSystemInfoCount} with System Info  •  " +
                $"{result.FailedCount} failed  •  {result.Roots.Count} root item(s)");
        }

        private void AddHierarchyTable(Section section, ExtractionResult result)
        {
            var table = section.AddTable();
            table.Borders.Width = 0;
            table.Rows.LeftIndent = 0;

            AddColumn(table, 1.7);   // ID
            AddColumn(table, 3.0);   // Type / State
            AddColumn(table, 8.5);   // Title
            AddColumn(table, 13.3);  // System Info

            AddHeaderRow(table);

            var first = true;
            foreach (var root in result.Roots)
            {
                foreach (var (node, depth) in Subtree(root))
                {
                    AddNodeRow(table, node, depth, isRoot: depth == 0, isFirstGroup: first && depth == 0);
                }

                first = false;
            }
        }

        private static void AddColumn(Table table, double centimeters)
        {
            var column = table.AddColumn(Unit.FromCentimeter(centimeters));
            column.Format.Alignment = ParagraphAlignment.Left;
        }

        private static void AddHeaderRow(Table table)
        {
            var row = table.AddRow();
            row.HeadingFormat = true;
            row.Shading.Color = HeaderShading;
            row.Format.Font.Color = Colors.White;
            row.Format.Font.Bold = true;
            row.Format.Font.Size = 7.5;
            row.TopPadding = row.BottomPadding = 3;

            SetCell(row.Cells[0], "ID");
            SetCell(row.Cells[1], "TYPE / STATE");
            SetCell(row.Cells[2], "TITLE");
            SetCell(row.Cells[3], "SYSTEM INFO");
        }

        private void AddNodeRow(Table table, WorkItemNode node, int depth, bool isRoot, bool isFirstGroup)
        {
            var row = table.AddRow();
            row.TopPadding = isRoot ? 5 : 3;
            row.BottomPadding = 3;

            foreach (Cell cell in row.Cells)
            {
                cell.Borders.Bottom.Width = 0.25;
                cell.Borders.Bottom.Color = Hairline;
            }

            if (isRoot)
            {
                row.Shading.Color = RootShading;
                if (!isFirstGroup)
                {
                    foreach (Cell cell in row.Cells)
                    {
                        cell.Borders.Top.Width = 1.25;
                        cell.Borders.Top.Color = GroupRule;
                    }
                }
            }

            // ID
            var id = row.Cells[0].AddParagraph("#" + node.Id);
            id.Format.Font.Bold = isRoot;

            // Type / State
            var typeState = row.Cells[1].AddParagraph();
            if (node.Error != null)
            {
                typeState.AddFormattedText("ERROR", TextFormat.Bold).Color = ErrorColor;
            }
            else
            {
                typeState.AddText(node.Type ?? "-");
                if (!string.IsNullOrEmpty(node.State))
                {
                    typeState.AddLineBreak();
                    var st = typeState.AddFormattedText(node.State);
                    st.Font.Color = Muted;
                    st.Font.Size = 7.5;
                }
            }

            // Title, indented by hierarchy depth
            var title = row.Cells[2].AddParagraph();
            title.Format.LeftIndent = Unit.FromCentimeter(0.42 * depth);
            title.Format.Font.Bold = isRoot;
            if (!isRoot)
            {
                var connector = title.AddFormattedText("└ ");
                connector.Font.Color = Muted;
            }
            title.AddText(string.IsNullOrEmpty(node.Title) ? "(untitled)" : node.Title);

            // System Info (+ source control, when present)
            AddSystemInfoCell(row.Cells[3], node);
        }

        private void AddSystemInfoCell(Cell cell, WorkItemNode node)
        {
            if (node.Error != null)
            {
                var err = cell.AddParagraph(node.Error);
                err.Format.Font.Color = ErrorColor;
                return;
            }

            if (string.IsNullOrEmpty(node.SystemInfo))
            {
                var none = cell.AddParagraph("no System Info");
                none.Format.Font.Italic = true;
                none.Format.Font.Color = Muted;
            }
            else
            {
                foreach (var line in node.SystemInfo!.Replace("\r\n", "\n").Split('\n'))
                {
                    cell.AddParagraph(line.Length == 0 ? " " : line);
                }
            }

            AddSourceControl(cell, node.SourceControl);
        }

        private void AddSourceControl(Cell cell, SourceControlInfo? sc)
        {
            if (sc == null || sc.IsEmpty)
            {
                return;
            }

            if (sc.Repositories.Count > 0)
            {
                AddScLine(cell, "Repositories", string.Join(", ", sc.Repositories.Select(r => r.Name)));
            }

            if (sc.Commits.Count > 0)
            {
                var commits = sc.Commits
                    .Take(8)
                    .Select(c => c.ShortId + (string.IsNullOrEmpty(c.Message) ? string.Empty : " " + FirstLine(c.Message!)));
                var text = string.Join("; ", commits);
                if (sc.Commits.Count > 8)
                {
                    text += $"; (+{sc.Commits.Count - 8} more)";
                }

                AddScLine(cell, "Commits", text);
            }

            if (sc.Contributors.Count > 0)
            {
                AddScLine(cell, "Changed by", string.Join(", ", sc.Contributors.Select(d => d.DisplayName ?? d.Name)));
            }
        }

        private static void AddScLine(Cell cell, string label, string value)
        {
            var p = cell.AddParagraph();
            p.Format.SpaceBefore = 2;
            p.Format.Font.Size = 7.5;
            p.Format.Font.Color = Muted;
            p.AddFormattedText(label + ": ", TextFormat.Bold);
            p.AddText(value);
        }

        private static void SetCell(Cell cell, string text)
        {
            cell.AddParagraph(text);
        }

        private static string FirstLine(string text)
        {
            var idx = text.IndexOfAny(new[] { '\r', '\n' });
            return idx < 0 ? text : text.Substring(0, idx);
        }

        private static IEnumerable<(WorkItemNode Node, int Depth)> Subtree(WorkItemNode root)
        {
            var buffer = new List<(WorkItemNode, int)>();

            void Walk(WorkItemNode node, int depth)
            {
                buffer.Add((node, depth));
                foreach (var child in node.Children)
                {
                    Walk(child, depth + 1);
                }
            }

            Walk(root, 0);
            return buffer;
        }
    }
}
