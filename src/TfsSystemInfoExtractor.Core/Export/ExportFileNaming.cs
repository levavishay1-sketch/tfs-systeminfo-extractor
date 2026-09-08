using System;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Export
{
    public static class ExportFileNaming
    {
        public static string ForResult(ExtractionResult result, string extension) =>
            $"TfsSystemInfo_{result.GeneratedAt:yyyy-MM-dd_HHmmss}.{extension}";

        public static string ForTimestamp(string extension) =>
            $"TfsSystemInfo_{DateTime.Now:yyyy-MM-dd_HHmmss}.{extension}";
    }
}
