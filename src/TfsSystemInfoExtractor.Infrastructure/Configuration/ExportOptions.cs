namespace TfsSystemInfoExtractor.Infrastructure.Configuration
{
    public sealed class ExportOptions
    {
        public const string SectionName = "Export";

        /// <summary>Folder that every rendered export is also written to.</summary>
        public string OutputDirectory { get; set; } = @"C:\TfsSystemInfoExport";
    }
}
