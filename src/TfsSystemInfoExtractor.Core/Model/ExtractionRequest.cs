using System;
using System.Collections.Generic;
using System.Linq;

namespace TfsSystemInfoExtractor.Core.Model
{
    /// <summary>The set of root work item ids to extract.</summary>
    public sealed class ExtractionRequest
    {
        public ExtractionRequest(IEnumerable<int> rootIds)
        {
            if (rootIds == null) throw new ArgumentNullException(nameof(rootIds));
            RootIds = rootIds.ToArray();
        }

        public IReadOnlyList<int> RootIds { get; }
    }
}
