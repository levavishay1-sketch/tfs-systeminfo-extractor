using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>Renders an <see cref="ExtractionResult"/> into one concrete output format.</summary>
    public interface IExportFormatter
    {
        ExportFormat Format { get; }

        ExportArtifact Render(ExtractionResult result);
    }
}
