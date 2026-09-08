using System;

namespace TfsSystemInfoExtractor.Core.Model.SourceControl
{
    /// <summary>
    /// A component touched by a work item's source-control changes - the top-level
    /// area of a repository (or a TFVC project sub-folder) that a commit modified.
    /// A work item can span several, so this is always held in a collection.
    /// </summary>
    public sealed class SourceComponent
    {
        public SourceComponent(string name, string? repositoryId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Component name is required.", nameof(name));
            }

            Name = name;
            RepositoryId = repositoryId;
        }

        public string Name { get; }

        /// <summary>The <see cref="SourceRepository.Id"/> this component belongs to, when known.</summary>
        public string? RepositoryId { get; }
    }
}
