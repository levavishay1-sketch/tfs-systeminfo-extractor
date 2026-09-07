namespace TfsSystemInfoExtractor.Infrastructure.Configuration
{
    /// <summary>How to reach the on-premises TFS / Azure DevOps Server collection.</summary>
    public sealed class TfsOptions
    {
        public const string SectionName = "Tfs";

        /// <summary>Collection URL, e.g. <c>http://server:8080/tfs/CollectionName</c>. No trailing slash needed.</summary>
        public string CollectionUrl { get; set; } = string.Empty;

        /// <summary>REST API version to request (e.g. "2.0", "3.0", "4.1").</summary>
        public string ApiVersion { get; set; } = "3.0";

        /// <summary>Display name of the custom field to extract.</summary>
        public string SystemInfoFieldDisplayName { get; set; } = "System Info";

        /// <summary>Link relation that identifies a child work item.</summary>
        public string ChildLinkRelation { get; set; } = "System.LinkTypes.Hierarchy-Forward";

        /// <summary>Per-request timeout, seconds.</summary>
        public int RequestTimeoutSeconds { get; set; } = 60;
    }
}
