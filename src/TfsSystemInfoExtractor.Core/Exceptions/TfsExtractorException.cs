using System;

namespace TfsSystemInfoExtractor.Core.Exceptions
{
    /// <summary>Base type for every error this application raises on purpose.</summary>
    public abstract class TfsExtractorException : Exception
    {
        protected TfsExtractorException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
