using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TfsSystemInfoExtractor.Core.Model.SourceControl
{
    /// <summary>
    /// All source-control activity linked to one work item. This is the single
    /// extension point for source-control data: further facets (pull requests,
    /// branches, builds, ...) are added here as new typed collections rather than as
    /// flat fields on <see cref="WorkItemNode"/>, so callers keep working and the
    /// model stays cohesive.
    /// </summary>
    public sealed class SourceControlInfo
    {
        public static readonly SourceControlInfo Empty = new SourceControlInfo();

        public SourceControlInfo(
            IEnumerable<SourceRepository>? repositories = null,
            IEnumerable<Commit>? commits = null,
            IEnumerable<Developer>? contributors = null)
        {
            Repositories = repositories?.ToArray() ?? Array.Empty<SourceRepository>();
            Commits = commits?.ToArray() ?? Array.Empty<Commit>();
            Contributors = contributors?.ToArray() ?? Array.Empty<Developer>();
        }

        /// <summary>Repositories the work item's changes live in.</summary>
        public IReadOnlyList<SourceRepository> Repositories { get; }

        /// <summary>Commits / changesets associated with the work item.</summary>
        public IReadOnlyList<Commit> Commits { get; }

        /// <summary>People who made the associated changes. Not necessarily derived from <see cref="Commits"/> — a
        /// reviewer or co-author may appear here without a commit of their own.</summary>
        public IReadOnlyList<Developer> Contributors { get; }

        [JsonIgnore]
        public bool IsEmpty => Repositories.Count == 0 && Commits.Count == 0 && Contributors.Count == 0;
    }
}
