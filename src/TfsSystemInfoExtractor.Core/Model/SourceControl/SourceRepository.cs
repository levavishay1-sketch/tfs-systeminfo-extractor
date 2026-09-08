using System;

namespace TfsSystemInfoExtractor.Core.Model.SourceControl
{
    /// <summary>
    /// A source-control repository a work item's changes live in. A work item may be
    /// linked to several repositories, so this is always held in a collection.
    /// </summary>
    public sealed class SourceRepository
    {
        public SourceRepository(string id, string name, string? url = null, string? kind = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Repository id is required.", nameof(id));
            }

            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            Url = url;
            Kind = kind;
        }

        /// <summary>Stable identifier used by <see cref="Commit.RepositoryId"/> to point back here.</summary>
        public string Id { get; }

        public string Name { get; }

        public string? Url { get; }

        /// <summary>Repository technology, e.g. <c>TfsGit</c>, <c>TfsVersionControl</c>, <c>GitHub</c>.</summary>
        public string? Kind { get; }
    }
}
