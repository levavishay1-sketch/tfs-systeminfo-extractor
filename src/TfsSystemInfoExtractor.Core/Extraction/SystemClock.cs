using System;
using TfsSystemInfoExtractor.Core.Abstractions;

namespace TfsSystemInfoExtractor.Core.Extraction
{
    public sealed class SystemClock : ISystemClock
    {
        public DateTimeOffset Now => DateTimeOffset.Now;
    }
}
