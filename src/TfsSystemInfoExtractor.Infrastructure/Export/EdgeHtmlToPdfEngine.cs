using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Export
{
    /// <summary>
    /// <see cref="IBrowserPdfEngine"/> backed by the machine's installed Edge / Chrome
    /// in headless "--print-to-pdf" mode. It only runs a browser process; it never
    /// downloads one, and it never touches the user's normal browser profile.
    /// </summary>
    public sealed class EdgeHtmlToPdfEngine : IBrowserPdfEngine
    {
        private readonly ExportOptions _options;
        private readonly ILogger<EdgeHtmlToPdfEngine> _logger;
        private readonly Lazy<string?> _browserPath;

        public EdgeHtmlToPdfEngine(IOptions<ExportOptions> options, ILogger<EdgeHtmlToPdfEngine> logger)
        {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _browserPath = new Lazy<string?>(() => ChromiumBrowserLocator.Locate(_options.BrowserPath));
        }

        public bool IsAvailable => _browserPath.Value != null;

        public string? BrowserPath => _browserPath.Value;

        public byte[] RenderPdf(string html, PdfPrintOptions printOptions)
        {
            if (html == null) throw new ArgumentNullException(nameof(html));
            printOptions = printOptions ?? PdfPrintOptions.Default;

            var browser = _browserPath.Value ?? throw new BrowserNotFoundException();

            var workDir = Path.Combine(Path.GetTempPath(), "tfs-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            var htmlPath = Path.Combine(workDir, "report.html");
            var pdfPath = Path.Combine(workDir, "report.pdf");
            var profileDir = Path.Combine(workDir, "profile");

            try
            {
                File.WriteAllText(htmlPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var args = string.Join(" ", new[]
                {
                    "--headless=new",
                    "--disable-gpu",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-extensions",
                    "--disable-sync",
                    "--font-render-hinting=none",
                    "--run-all-compositor-stages-before-draw",
                    "--virtual-time-budget=8000",
                    Quote("--user-data-dir=" + profileDir),
                    "--no-pdf-header-footer",
                    Quote("--print-to-pdf=" + pdfPath),
                    Quote(new Uri(htmlPath).AbsoluteUri)
                });

                RunBrowser(browser, args);

                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 400)
                {
                    throw new PdfRenderException("The browser did not produce a PDF. Check that headless printing is allowed by policy.");
                }

                return File.ReadAllBytes(pdfPath);
            }
            catch (PdfRenderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PdfRenderException("PDF generation failed: " + ex.Message, ex);
            }
            finally
            {
                TryDelete(workDir);
            }
        }

        private void RunBrowser(string browser, string arguments)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.PdfTimeoutSeconds));
            var startInfo = new ProcessStartInfo(browser, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            process.OutputDataReceived += (_, __) => { };

            if (!process.Start())
            {
                throw new PdfRenderException("Could not start the browser process at " + browser);
            }

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* best effort */ }
                throw new PdfRenderException($"PDF generation timed out after {timeout.TotalSeconds:0}s.");
            }

            // headless Chromium prints diagnostic noise to stderr and still exits 0; only a
            // non-zero exit combined with no output file is a real failure (checked by the caller).
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Browser exited with code {ExitCode}. stderr tail: {Stderr}",
                    process.ExitCode, Tail(stderr.ToString()));
            }
        }

        private static string Quote(string value) => "\"" + value + "\"";

        private static string Tail(string text) =>
            string.IsNullOrEmpty(text) || text.Length <= 500 ? text : text.Substring(text.Length - 500);

        private void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not clean up temp PDF directory {Dir}", dir);
            }
        }
    }
}
