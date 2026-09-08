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
        /// Also fetch each commit's changed items (the real source-control data) to
        /// determine the specific components it changed. Costs one extra call per commit;
        /// turn off if extraction is too slow.
        /// </summary>
        public bool IncludeComponents { get; set; } = true;

        /// <summary>
        /// Container-folder names whose <em>immediate child</em> is a component, e.g. a
        /// change under <c>.../Components/ComponentA/...</c> is attributed to
        /// <c>ComponentA</c>. Matched case-insensitively anywhere in the changed item's
        /// path. When no container matches, the component is the folder the change was
        /// actually made in (the changed item's own directory) - never a truncated
        /// ancestor path.
        /// </summary>
        public string[] ComponentContainerFolders { get; set; } = { "Components" };

        /// <summary>Cap on how many changed item paths are kept per commit for display.</summary>
        public int MaxChangedPathsPerCommit { get; set; } = 25;
    }
}
