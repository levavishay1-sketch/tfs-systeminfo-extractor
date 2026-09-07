using System;
using System.Text;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>A rendered export ready to be written to disk or streamed to the browser.</summary>
    public sealed class ExportArtifact
    {
        public ExportArtifact(ExportFormat format, string fileName, string contentType, byte[] content)
        {
            Format = format;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public ExportFormat Format { get; }

        public string FileName { get; }

        public string ContentType { get; }

        public byte[] Content { get; }

        /// <summary>Creates an artifact from UTF-8 text, optionally prefixed with a BOM (needed by Excel for CSV).</summary>
        public static ExportArtifact FromText(ExportFormat format, string fileName, string contentType, string text, bool withBom = false)
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var body = utf8.GetBytes(text ?? string.Empty);

            byte[] content;
            if (withBom)
            {
                content = new byte[3 + body.Length];
                content[0] = 0xEF;
                content[1] = 0xBB;
                content[2] = 0xBF;
                System.Buffer.BlockCopy(body, 0, content, 3, body.Length);
            }
            else
            {
                content = body;
            }

            return new ExportArtifact(format, fileName, contentType, content);
        }
    }
}
