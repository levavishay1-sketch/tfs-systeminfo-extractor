namespace TfsSystemInfoExtractor.Infrastructure.Configuration
{
    public sealed class ExportOptions
    {
        public const string SectionName = "Export";

        /// <summary>Folder a copy of every export is written to when the user clicks an export action.</summary>
        public string OutputDirectory { get; set; } = @"C:\TfsSystemInfoExport";

        /// <summary>
        /// Optional explicit path to the Chromium-based browser used for PDF export
        /// (msedge.exe or chrome.exe). Leave empty to auto-detect the installed Edge / Chrome.
        /// </summary>
        public string BrowserPath { get; set; } = string.Empty;

        /// <summary>Hard timeout for a single PDF render.</summary>
        public int PdfTimeoutSeconds { get; set; } = 40;
    }
}
