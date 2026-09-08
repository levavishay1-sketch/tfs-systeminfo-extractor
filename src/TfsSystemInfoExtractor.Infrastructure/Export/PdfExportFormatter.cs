using System;
using System.IO;
using MigraDoc.Rendering;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Infrastructure.Export
{
    /// <summary>
    /// Renders the extraction result as a formatted PDF release report. Implements the
    /// same <see cref="IExportFormatter"/> port as the JSON / Markdown / CSV formatters,
    /// so it is picked up automatically by the export pipeline; the PDF-specific
    /// dependency (PDFsharp / MigraDoc) is confined to this Infrastructure assembly.
    /// </summary>
    public sealed class PdfExportFormatter : IExportFormatter
    {
        // PDFsharp's GDI font handling is not thread-safe; extractions are rare and PDF
        // generation is fast, so serialising it here is a cheap safety net.
        private static readonly object RenderGate = new object();

        private readonly PdfReportDocumentBuilder _documentBuilder;

        public PdfExportFormatter(PdfReportDocumentBuilder documentBuilder)
        {
            _documentBuilder = documentBuilder ?? throw new ArgumentNullException(nameof(documentBuilder));
        }

        public ExportFormat Format => ExportFormat.Pdf;

        public ExportArtifact Render(ExtractionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var document = _documentBuilder.Build(result);

            byte[] bytes;
            lock (RenderGate)
            {
                var renderer = new PdfDocumentRenderer(unicode: true) { Document = document };
                renderer.RenderDocument();

                using var stream = new MemoryStream();
                renderer.PdfDocument.Save(stream, closeStream: false);
                bytes = stream.ToArray();
            }

            return new ExportArtifact(
                ExportFormat.Pdf,
                ExportFileNaming.ForResult(result, "pdf"),
                "application/pdf",
                bytes);
        }
    }
}
