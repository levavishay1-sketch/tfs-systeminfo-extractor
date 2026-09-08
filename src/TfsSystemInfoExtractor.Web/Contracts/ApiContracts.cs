using System;
using System.Collections.Generic;
using TfsSystemInfoExtractor.Core.Reporting;

namespace TfsSystemInfoExtractor.Web.Contracts
{
    /// <summary>Response to <c>POST /api/run</c>. Serialized camelCase; the browser reads <c>data.jobId</c>.</summary>
    public sealed class StartResponse
    {
        public StartResponse(string jobId) => JobId = jobId;

        public string JobId { get; }
    }

    /// <summary>Response to <c>GET /api/status</c>. Field names match what the browser JS expects.</summary>
    public sealed class StatusResponse
    {
        public StatusResponse(bool done, int processed, string? error, IReadOnlyList<string> log, bool hasResult)
        {
            Done = done;
            Processed = processed;
            Error = error;
            Log = log ?? Array.Empty<string>();
            HasResult = hasResult;
        }

        public bool Done { get; }

        public int Processed { get; }

        public string? Error { get; }

        public IReadOnlyList<string> Log { get; }

        public bool HasResult { get; }
    }

    /// <summary>
    /// Body of <c>POST /api/export</c>. The browser sends the format token plus the
    /// exact view it is currently showing. The server renders that and nothing else.
    /// </summary>
    public sealed class ExportRequest
    {
        public string Format { get; set; } = string.Empty;

        public ReportView? View { get; set; }
    }
}
