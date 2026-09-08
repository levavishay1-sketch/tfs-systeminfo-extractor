namespace TfsSystemInfoExtractor.Infrastructure.Configuration
{
    /// <summary>
    /// How to resolve the source-control activity linked to a work item (commits /
    /// changesets discovered from its <c>ArtifactLink</c> relations).
    /// </summary>
    public sealed class TfsSourceControlOptions
    {
        public const string SectionName = "Tfs:SourceControl";

        /// <summary>Turn the whole feature off (no version-control API calls) when false.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Safety cap on how many linked commits/changesets are resolved per work item.</summary>
        public int MaxCommitsPerWorkItem { get; set; } = 50;

        /// <summary>
        /// Also fetch each commit's changed paths to derive the components it touched.
        /// Costs one extra call per commit; turn off if extraction is too slow.
        /// </summary>
        public bool IncludeComponents { get; set; } = true;

        /// <summary>
        /// Which path segment (1-based, relative to the repository / TFVC project root)
        /// names a component. 1 = the top-level folder.
        /// </summary>
        public int ComponentPathDepth { get; set; } = 1;
    }
}
