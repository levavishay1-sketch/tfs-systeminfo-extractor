namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>
    /// A raw artifact link on a work item (a TFS <c>ArtifactLink</c> relation): the
    /// <c>vstfs:///</c> URI that points at a commit, changeset, pull request or branch,
    /// plus the link's display name. A source-control provider interprets these.
    /// </summary>
    public sealed class WorkItemArtifactLink
    {
        public WorkItemArtifactLink(string uri, string? linkName = null)
        {
            Uri = uri ?? string.Empty;
            LinkName = linkName;
        }

        /// <summary>e.g. <c>vstfs:///Git/Commit/{project}%2F{repo}%2F{sha}</c>.</summary>
        public string Uri { get; }

        /// <summary>e.g. "Fixed in Commit", "Fixed in Changeset", "Pull Request".</summary>
        public string? LinkName { get; }
    }
}
