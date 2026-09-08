using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TfsSystemInfoExtractor.Core.Model.SourceControl
{
    /// <summary>
    /// A single commit / changeset associated with a work item. A work item may have
    /// many, across several repositories, so this is always held in a collection.
    /// </summary>
    public sealed class Commit
    {
        public Commit(
            string id,
            string? message = null,
            Developer? author = null,
            DateTimeOffset? committedOn = null,
            string? repositoryId = null,
            string? url = null,
            IEnumerable<string>? components = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Commit id is required.", nameof(id));
            }

            Id = id;
            Message = message;
            Author = author;
            CommittedOn = committedOn;
            RepositoryId = repositoryId;
            Url = url;
            Components = components?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
        }

        /// <summary>Full commit hash or changeset id.</summary>
        public string Id { get; }

        public string? Message { get; }

        public Developer? Author { get; }

        public DateTimeOffset? CommittedOn { get; }

        /// <summary>Matches a <see cref="SourceRepository.Id"/> in the same <see cref="SourceControlInfo"/>.</summary>
        public string? RepositoryId { get; }

        public string? Url { get; }

        /// <summary>Names of the components (top-level repository areas) this commit changed.</summary>
        public IReadOnlyList<string> Components { get; }

        [JsonIgnore]
        public string ShortId => Id.Length <= 8 ? Id : Id.Substring(0, 8);
    }
}
