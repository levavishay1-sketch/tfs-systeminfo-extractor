using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.Export;
using TfsSystemInfoExtractor.Tests.Fakes;
using TfsSystemInfoExtractor.Web.Export;
using TfsSystemInfoExtractor.Web.Ui;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Web
{
    public class HtmlToPdfReportRendererTests
    {
        private sealed class FakePdfEngine : IBrowserPdfEngine
        {
            public string? LastHtml { get; private set; }
            public bool IsAvailable => true;
            public string? BrowserPath => "fake";

            public byte[] RenderPdf(string html, PdfPrintOptions options)
            {
                LastHtml = html;
                return Encoding.ASCII.GetBytes("%PDF-1.7 fake");
            }
        }

        private static HtmlToPdfReportRenderer New(FakePdfEngine engine) =>
            new HtmlToPdfReportRenderer(engine, new EmbeddedUiAssetProvider());

        [Fact]
        public void Wraps_the_views_own_html_in_the_pages_stylesheet_and_a_print_override()
        {
            var engine = new FakePdfEngine();
            var view = ReportViewBuilder.Sample();
            view.Html = "<div class=\"tv-wrap\"><table class=\"tv\"><tr><td>ROW-MARKER</td></tr></table></div>";

            var artifact = New(engine).Render(view);

            Assert.Equal(ExportFormat.Pdf, artifact.Format);
            Assert.EndsWith(".pdf", artifact.FileName);

            var html = engine.LastHtml!;
            Assert.Contains("<meta charset=\"utf-8\">", html);
            Assert.Contains("ROW-MARKER", html);                        // the exact table markup, verbatim
            Assert.Contains(".view-table", html);                       // the page's own stylesheet is inlined
            Assert.Contains("class=\"viewport view-table pdf-export\"", html);
            Assert.Contains("<h1>TFS System Info</h1>", html);
        }

        [Fact]
        public void Composes_a_single_continuous_page_sized_to_the_measured_content()
        {
            var engine = new FakePdfEngine();
            var view = ReportViewBuilder.Sample();
            view.LayoutWidthPx = 1240;

            New(engine).Render(view);
            var html = engine.LastHtml!;

            // a rewritable @page placeholder, no fixed A4 / landscape page and no page breaks
            Assert.Contains("<style id=\"pdf-page-size\">@page { margin: 0; }</style>", html);
            Assert.DoesNotContain("A4", html);
            Assert.DoesNotContain("break-inside", html);
            // the fit script targets that placeholder and sets an explicit px page size
            Assert.Contains("getElementById('pdf-page-size')", html);
            Assert.Contains("@page { size: ' + w + 'px ' + h + 'px", html);
            // the table is laid out at the on-screen width (+ body padding) so wrapping matches
            Assert.Contains("body.pdf-export { width: 1280px; }", html);
        }

        [Fact]
        public void Falls_back_to_a_default_layout_width_when_the_client_did_not_report_one()
        {
            var engine = new FakePdfEngine();
            New(engine).Render(ReportViewBuilder.Sample());

            Assert.Contains("body.pdf-export { width: 1140px; }", engine.LastHtml!); // 1100 default + 40 padding
        }

        [Fact]
        public void Injects_the_captured_theme_tokens_so_the_pdf_matches_the_users_appearance()
        {
            var engine = new FakePdfEngine();
            var view = ReportViewBuilder.Sample();
            view.ThemeCss = ":root{--bg:#0e1014;--text:#e6e8ec;--tv-root-bg:#1e222c;}";

            New(engine).Render(view);

            Assert.Contains(":root{--bg:#0e1014;--text:#e6e8ec;--tv-root-bg:#1e222c;}", engine.LastHtml!);
        }

        [Fact]
        public void Theme_tokens_cannot_break_out_of_the_style_element()
        {
            var engine = new FakePdfEngine();
            var view = ReportViewBuilder.Sample();
            view.ThemeCss = ":root{--x:red}</style><script>alert(1)</script>";

            New(engine).Render(view);

            Assert.DoesNotContain("<script>alert(1)</script>", engine.LastHtml!);
        }

        [Fact]
        public void Strips_any_script_from_the_supplied_html()
        {
            var engine = new FakePdfEngine();
            var view = ReportViewBuilder.Sample();
            view.Html = "<table class=\"tv\"><tr><td>ok</td></tr></table><script>alert(1)</script>";

            New(engine).Render(view);

            // the client-supplied script is gone; only the renderer's own fit-to-one-page script remains
            Assert.DoesNotContain("alert(1)", engine.LastHtml!);
            Assert.Contains("ok", engine.LastHtml!);
        }

        [Fact]
        public void End_to_end_with_the_real_browser_when_available()
        {
            var engine = new EdgeHtmlToPdfEngine(Options.Create(new ExportOptions()), NullLogger<EdgeHtmlToPdfEngine>.Instance);
            if (!engine.IsAvailable)
            {
                return;
            }

            var view = ReportViewBuilder.Sample("Priority");
            var real = new HtmlToPdfReportRenderer(engine, new EmbeddedUiAssetProvider()).Render(view);

            Assert.Equal("%PDF-", Encoding.ASCII.GetString(real.Content, 0, 5));
            Assert.True(real.Content.Length > 2000);
        }
    }
}
