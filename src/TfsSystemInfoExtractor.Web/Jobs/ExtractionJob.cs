using System;
using System.Collections.Generic;
using System.Threading;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Web.Jobs
{
    public enum JobStatus
    {
        Running,
        Completed,
        Faulted
    }

    /// <summary>
    /// Mutable state of one background extraction: a progress log, a processed count,
    /// and - once finished - the <see cref="ExtractionResult"/> (or an error). No
    /// exports are held here; they are rendered on demand when the user clicks an
    /// export action. All members are safe to read from the polling HTTP thread while
    /// the worker writes.
    /// </summary>
    public sealed class ExtractionJob
    {
        private readonly object _logLock = new object();
        private readonly List<string> _log = new List<string>();
        private volatile JobStatus _status = JobStatus.Running;
        private int _processedCount;
        private volatile string? _error;
        private volatile ExtractionResult? _result;
        private volatile bool _authRequired;
        private volatile bool _credentialsRejected;

        public ExtractionJob(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Id { get; }

        public JobStatus Status => _status;

        public bool IsDone => _status != JobStatus.Running;

        public int ProcessedCount => Volatile.Read(ref _processedCount);

        public string? Error => _error;

        public ExtractionResult? Result => _result;

        public bool HasResult => _result != null;

        /// <summary>The run stopped on a 401 from Azure DevOps; the UI should prompt for a sign-in and re-run.</summary>
        public bool AuthRequired => _authRequired;

        /// <summary>True when an explicit username/password was already tried and still got a 401.</summary>
        public bool CredentialsRejected => _credentialsRejected;

        public IReadOnlyList<string> LogSnapshot
        {
            get
            {
                lock (_logLock)
                {
                    return _log.ToArray();
                }
            }
        }

        public void AppendLog(int depth, string message)
        {
            var line = new string(' ', Math.Max(0, depth) * 4) + message;
            lock (_logLock)
            {
                _log.Add(line);
            }
        }

        public void SetProcessedCount(int count) => Volatile.Write(ref _processedCount, count);

        public void Complete(ExtractionResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
            _status = JobStatus.Completed;
        }

        public void Fail(string message)
        {
            _error = message;
            _status = JobStatus.Faulted;
        }

        /// <summary>
        /// End the run because Azure DevOps returned 401. No <see cref="Error"/> is set - the
        /// UI shows a sign-in dialog instead of a raw message, then re-runs.
        /// </summary>
        public void RequireCredentials(bool credentialsRejected)
        {
            _authRequired = true;
            _credentialsRejected = credentialsRejected;
            _status = JobStatus.Faulted;
        }
    }
}
