using System;

namespace TfsSystemInfoExtractor.Core.Exceptions
{
    /// <summary>PDF generation failed (browser error, timeout, or no output produced).</summary>
    public sealed class PdfRenderException : TfsExtractorException
    {
        public PdfRenderException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>No local Chromium-based browser (Edge or Chrome) could be found for PDF export.</summary>
    public sealed class BrowserNotFoundException : TfsExtractorException
    {
        public BrowserNotFoundException()
            : base("PDF export needs Microsoft Edge or Google Chrome, which was not found on this machine. " +
                   "CSV and Markdown export do not require a browser. " +
                   "Set Export:BrowserPath in appsettings.json to point at msedge.exe or chrome.exe if it is installed elsewhere.")
        {
        }
    }
}
