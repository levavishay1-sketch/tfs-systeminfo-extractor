using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.Export;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Infrastructure
{
    /// <summary>
    /// Integration test: uses the machine's real Edge/Chrome. When no browser is
    /// present the test asserts the engine reports itself unavailable and stops.
    /// </summary>
    public class EdgeHtmlToPdfEngineTests
    {
        private static EdgeHtmlToPdfEngine NewEngine() =>
            new EdgeHtmlToPdfEngine(Options.Create(new ExportOptions()), NullLogger<EdgeHtmlToPdfEngine>.Instance);

        private const string HebrewHtml = @"<!doctype html><html lang='he' dir='rtl'><head><meta charset='utf-8'>
<style>@page{size:A4 landscape;margin:10mm} body{font-family:'Segoe UI',Arial,sans-serif}</style></head>
<body>
<h1 dir='auto'>דוח מערכת - System Info Report</h1>
<table border='1'>
<tr><td dir='auto'>שדרוג פורטל הקליטה של עובדים חדשים</td><td>Active</td></tr>
<tr><td dir='auto'>אימות טופס כתובת - כולל ZIP code באנגלית</td><td>Resolved</td></tr>
<tr><td dir='auto'>משתמש: dept\avishay, תאריך: 08/09/2026 14:30, סכום: $50,000</td><td>Closed</td></tr>
</table>
</body></html>";

        [Fact]
        public void Renders_hebrew_and_mixed_content_to_a_valid_pdf_with_selectable_text()
        {
            var engine = NewEngine();

            if (!engine.IsAvailable)
            {
                Assert.Null(engine.BrowserPath);
                return; // no browser on this machine - nothing more to check
            }

            var bytes = engine.RenderPdf(HebrewHtml, PdfPrintOptions.LandscapeReport);
            var latin1 = Encoding.GetEncoding(28591).GetString(bytes);

            Assert.True(bytes.Length > 2000);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.Contains("/ToUnicode", latin1);          // text is selectable / searchable
            Assert.Matches("/FontFile2|/FontFile3|/FontFile", latin1); // fonts embedded
            Assert.Contains("/MediaBox [0 0 841", latin1);   // A4 landscape (~841.9 x 594.9 pt)
        }
    }
}
