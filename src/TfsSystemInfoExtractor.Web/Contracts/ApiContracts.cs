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
        public StatusResponse(bool done, int processed, string? error, IReadOnlyList<string> log, bool hasResult,
            bool authRequired, bool credentialsRejected)
        {
            Done = done;
            Processed = processed;
            Error = error;
            Log = log ?? Array.Empty<string>();
            HasResult = hasResult;
            AuthRequired = authRequired;
            CredentialsRejected = credentialsRejected;
        }

        public bool Done { get; }

        public int Processed { get; }

        public string? Error { get; }

        public IReadOnlyList<string> Log { get; }

        public bool HasResult { get; }

        /// <summary>The run stopped on a 401; the UI shows the sign-in dialog and re-runs.</summary>
        public bool AuthRequired { get; }

        /// <summary>The previously entered username/password was itself rejected with a 401.</summary>
        public bool CredentialsRejected { get; }
    }

    /// <summary>Body of <c>POST /api/tfs-credentials</c>. The password is used once and never stored or logged.</summary>
    public sealed class CredentialsRequest
    {
        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
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
