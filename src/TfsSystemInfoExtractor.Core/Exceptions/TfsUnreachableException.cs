using System;

namespace TfsSystemInfoExtractor.Core.Exceptions
{
    /// <summary>The TFS collection itself could not be reached (DNS, connectivity, auth handshake).</summary>
    public sealed class TfsUnreachableException : TfsExtractorException
    {
        public TfsUnreachableException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
