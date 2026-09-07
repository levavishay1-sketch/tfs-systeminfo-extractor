using System;

namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>Abstracts <see cref="DateTimeOffset.Now"/> so timestamps are testable.</summary>
    public interface ISystemClock
    {
        DateTimeOffset Now { get; }
    }
}
