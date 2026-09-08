using System;

namespace TfsSystemInfoExtractor.Core.Model.SourceControl
{
    /// <summary>
    /// A person who contributed source-control changes to a work item. Kept as its own
    /// type (rather than a bare string) so identity, display name and account can grow
    /// independently and a work item can reference several of them.
    /// </summary>
    public sealed class Developer
    {
        public Developer(string name, string? uniqueName = null, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Developer name is required.", nameof(name));
            }

            Name = name;
            UniqueName = uniqueName;
            DisplayName = displayName;
        }

        /// <summary>Canonical account, e.g. <c>DOMAIN\jdoe</c>.</summary>
        public string Name { get; }

        /// <summary>Unique login / UPN / email, when known.</summary>
        public string? UniqueName { get; }

        /// <summary>Human-friendly name, e.g. "Jane Doe".</summary>
        public string? DisplayName { get; }
    }
}
