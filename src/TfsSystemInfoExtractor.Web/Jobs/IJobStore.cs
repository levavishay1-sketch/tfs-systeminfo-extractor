using System.Collections.Concurrent;

namespace TfsSystemInfoExtractor.Web.Jobs
{
    public interface IJobStore
    {
        void Add(ExtractionJob job);

        bool TryGet(string id, out ExtractionJob job);
    }

    /// <summary>Process-lifetime, in-memory job registry. Adequate for a single-user local tool.</summary>
    public sealed class InMemoryJobStore : IJobStore
    {
        private readonly ConcurrentDictionary<string, ExtractionJob> _jobs = new ConcurrentDictionary<string, ExtractionJob>();

        public void Add(ExtractionJob job) => _jobs[job.Id] = job;

        public bool TryGet(string id, out ExtractionJob job) => _jobs.TryGetValue(id ?? string.Empty, out job!);
    }
}
