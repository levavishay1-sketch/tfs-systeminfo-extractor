namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>
    /// Turns a complete HTML document into PDF bytes using a local Chromium-based
    /// browser (Edge or Chrome) in headless "print to PDF" mode. No layout logic of
    /// its own - the browser renders the supplied HTML/CSS exactly as it would on screen.
    /// </summary>
    public interface IBrowserPdfEngine
    {
        /// <summary>Whether a usable browser was found on this machine.</summary>
        bool IsAvailable { get; }

        /// <summary>Path to the browser executable that will be used, or null when none was found.</summary>
        string? BrowserPath { get; }

        /// <summary>
        /// Renders <paramref name="html"/> (a full, self-contained HTML document) to PDF.
        /// Throws <see cref="Exceptions.BrowserNotFoundException"/> or
        /// <see cref="Exceptions.PdfRenderException"/> on failure.
        /// </summary>
        byte[] RenderPdf(string html, PdfPrintOptions options);
    }

    public sealed class PdfPrintOptions
    {
        public static readonly PdfPrintOptions LandscapeReport = new PdfPrintOptions { Landscape = true };

        public bool Landscape { get; set; } = true;
    }
}
