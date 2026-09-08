using System;
using System.Text;
using System.Text.RegularExpressions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Web.Ui;

namespace TfsSystemInfoExtractor.Web.Export
{
    /// <summary>
    /// PDF renderer that reuses the UI itself: it wraps the exact table markup the
    /// browser is showing (<see cref="ReportView.Html"/>) in the page's own stylesheet
    /// plus a small print-only override, and hands that to <see cref="IBrowserPdfEngine"/>.
    /// There is no PDF-specific table renderer and no re-derivation of what to show -
    /// the browser already decided, and Chromium renders it (Hebrew/RTL included).
    /// </summary>
    public sealed class HtmlToPdfReportRenderer : IReportRenderer
    {
        // The print sheet only does two things: page setup, and making the on-screen
        // table (which scrolls horizontally and clamps long cells) fit a printed page.
        // It changes no colours, borders, spacing or structure - that all comes from
        // the page's own stylesheet applied to the exact table markup.
        private const string PrintCss = @"
@page { size: A4 landscape; margin: 8mm 7mm 11mm; }
html, body { background: var(--bg, #fff) !important; margin: 0; }
body.pdf-export { padding: 0; }
body.pdf-export > .print-header { margin: 0 0 8px; }
body.pdf-export > .print-header h1 { margin: 0; font-size: 15px; font-weight: 700; }
body.pdf-export > .print-header .print-sub { font-size: 10.5px; color: var(--muted, #555); margin-top: 2px; }
/* fit the page: no horizontal scroll, long cells not clamped. The table keeps its
   fixed layout and the columns keep their percentage widths, so proportions match the UI. */
.view-table .tv-wrap { overflow: visible !important; width: auto !important; }
.view-table table.tv { width: 100% !important; min-width: 0 !important; }
.view-table .tv thead { display: table-header-group; }
.view-table .tv thead th { position: static !important; }
.view-table .tv tr { break-inside: avoid; }
.view-table .tv-si.clamped .body { max-height: none !important; -webkit-mask-image: none !important; mask-image: none !important; }
.view-table .tv-si .more { display: none !important; }
* { -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; }
";

        private static readonly Regex StyleBlock =
            new Regex(@"<style[^>]*>(?<css>.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex ScriptBlock =
            new Regex(@"<script[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly IBrowserPdfEngine _engine;
        private readonly Lazy<string> _pageCss;

        public HtmlToPdfReportRenderer(IBrowserPdfEngine engine, IUiAssetProvider ui)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _pageCss = new Lazy<string>(() => ExtractPageCss(Encoding.UTF8.GetString(ui.IndexHtml)));
        }

        public ExportFormat Format => ExportFormat.Pdf;

        public ExportArtifact Render(ReportView view)
        {
            view.Validate();
            var html = Compose(view);
            var bytes = _engine.RenderPdf(html, PdfPrintOptions.LandscapeReport);
            return new ExportArtifact(
                ExportFormat.Pdf,
                ExportFileNaming.ForTimestamp("pdf"),
                "application/pdf",
                bytes);
        }

        private string Compose(ReportView view)
        {
            var body = ScriptBlock.Replace(view.Html ?? string.Empty, string.Empty);

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>").Append(Escape(view.Title)).Append("</title>");
            sb.Append("<style>").Append(_pageCss.Value).Append("</style>");
            if (!string.IsNullOrWhiteSpace(view.ThemeCss))
            {
                // the exact light/dark tokens resolved on the user's page, so the PDF matches what they see
                sb.Append("<style>").Append(view.ThemeCss!.Replace("<", string.Empty).Replace(">", string.Empty)).Append("</style>");
            }

            sb.Append("<style>").Append(PrintCss).Append("</style>");
            sb.Append("</head><body class=\"viewport view-table pdf-export\">");

            sb.Append("<div class=\"print-header\"><h1>").Append(Escape(view.Title)).Append("</h1>");
            var sub = string.Empty;
            if (!string.IsNullOrEmpty(view.GeneratedAt)) sub = "Generated " + view.GeneratedAt;
            if (!string.IsNullOrEmpty(view.Subtitle))
            {
                sub = sub.Length == 0 ? view.Subtitle! : sub + "  ·  " + view.Subtitle;
            }

            if (sub.Length > 0)
            {
                sb.Append("<div class=\"print-sub\">").Append(Escape(sub)).Append("</div>");
            }

            sb.Append("</div>");
            sb.Append(body);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string ExtractPageCss(string indexHtml)
        {
            var match = StyleBlock.Match(indexHtml);
            return match.Success ? match.Groups["css"].Value : string.Empty;
        }

        private static string Escape(string? value) => (value ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
