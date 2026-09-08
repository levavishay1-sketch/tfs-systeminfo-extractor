using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>
    /// Renders a prepared <see cref="ReportView"/> into one concrete output format.
    /// This is the single export seam: a new format is a new implementation, with no
    /// change to the domain, the extraction pipeline or the UI's view logic.
    /// </summary>
    public interface IReportRenderer
    {
        ExportFormat Format { get; }

        ExportArtifact Render(ReportView view);
    }
}
