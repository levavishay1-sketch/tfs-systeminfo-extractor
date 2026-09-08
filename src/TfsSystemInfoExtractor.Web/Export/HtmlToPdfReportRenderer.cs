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
    /// <para>
    /// The output is a single continuous page: the composed document lays the table out
    /// at the same width as the on-screen results area, then a tiny inline script measures
    /// the rendered content and sets <c>@page { size }</c> to exactly that, so the whole
    /// table stays on one page with no page breaks between rows or groups.
    /// </para>
    /// </summary>
    public sealed class HtmlToPdfReportRenderer : IReportRenderer
    {
        // Used when the client did not report the on-screen width.
        private const int DefaultLayoutWidthPx = 1100;

        // Chromium / the PDF format cap a page at 200 inches (14400pt ~= 19200px). Stay
        // safely under it: if the table is somehow taller/wider than this, the fit script
        // scales the whole document down so it still lands on ONE page.
        private const int MaxPageDimensionPx = 18000;

        // The print sheet only does page setup and un-clamps the on-screen "show more"
        // truncation of long System Info cells. It changes no colours, borders, spacing,
        // fonts or structure - that all comes from the page's own stylesheet applied to
        // the exact table markup.
        private const string PrintCss = @"
html, body { background: var(--bg, #fff) !important; margin: 0; }
body.pdf-export { padding: 18px 20px; }
body.pdf-export > .print-header { margin: 0 0 10px; }
body.pdf-export > .print-header h1 { margin: 0; font-size: 15px; font-weight: 700; }
body.pdf-export > .print-header .print-sub { font-size: 10.5px; color: var(--muted, #555); margin-top: 2px; }
.view-table .tv-wrap { overflow: visible !important; }
.view-table .tv thead th { position: static !important; }
.view-table .tv-si.clamped .body { max-height: none !important; -webkit-mask-image: none !important; mask-image: none !important; }
.view-table .tv-si .more { display: none !important; }
* { -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; }
";

        // Runs synchronously while the document parses (before the print snapshot): it
        // measures the fully laid-out content and rewrites the @page rule so the PDF is
        // one page exactly as tall and as wide as the table needs. If the content is
        // bigger than a PDF page can be, it scales the whole document down to fit - still
        // one page, never a page break.
        private const string FitToOnePageScript = @"
(function () {
  var doc = document.documentElement, b = document.body, cap = __CAP__;
  function measure() {
    return {
      w: Math.max(b.scrollWidth, doc.scrollWidth, __W__),
      h: Math.max(b.scrollHeight, doc.scrollHeight, b.offsetHeight)
    };
  }
  var m = measure();
  if (m.h > cap || m.w > cap) {
    b.style.zoom = Math.min(cap / m.h, cap / m.w) * 0.9;
    m = measure();
  }
  var w = Math.min(Math.ceil(m.w) + 2, cap);
  var h = Math.min(Math.ceil(m.h) + 40, cap);
  var s = document.getElementById('pdf-page-size');
  if (s) { s.textContent = '@page { size: ' + w + 'px ' + h + 'px; margin: 0; }'; }
})();
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
            var bytes = _engine.RenderPdf(html, PdfPrintOptions.Default);
            return new ExportArtifact(
                ExportFormat.Pdf,
                ExportFileNaming.ForTimestamp("pdf"),
                "application/pdf",
                bytes);
        }

        private string Compose(ReportView view)
        {
            var body = ScriptBlock.Replace(view.Html ?? string.Empty, string.Empty);
            var layoutWidth = view.LayoutWidthPx is int w && w > 200 ? w : DefaultLayoutWidthPx;

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
            // lay the table out at the same content width the user is looking at (plus the
            // body padding, since box-sizing is border-box), so it wraps identically
            sb.Append("<style>body.pdf-export { width: ").Append(layoutWidth + 40).Append("px; }</style>");
            // placeholder the fit-to-one-page script rewrites once the content is measured
            sb.Append("<style id=\"pdf-page-size\">@page { margin: 0; }</style>");
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
            sb.Append("<script>")
              .Append(FitToOnePageScript
                  .Replace("__W__", layoutWidth.ToString())
                  .Replace("__CAP__", MaxPageDimensionPx.ToString()))
              .Append("</script>");
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
