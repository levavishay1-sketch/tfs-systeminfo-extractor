using System;

namespace TfsSystemInfoExtractor.Core.Exceptions
{
    public enum WorkItemAccessError
    {
        NotFound,
        Forbidden,
        Network,
        Protocol,
        Server
    }

    /// <summary>
    /// A single work item could not be read. The hierarchy walker catches this and
    /// records the item as a failed node rather than aborting the whole run.
    /// </summary>
    public sealed class WorkItemAccessException : TfsExtractorException
    {
        public WorkItemAccessException(int workItemId, WorkItemAccessError error, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            WorkItemId = workItemId;
            Error = error;
        }

        public int WorkItemId { get; }

        public WorkItemAccessError Error { get; }
    }
}
