using TfsSystemInfoExtractor.Web.Jobs;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Web
{
    public class InMemoryJobStoreTests
    {
        [Fact]
        public void Round_trips_a_job_by_id()
        {
            var store = new InMemoryJobStore();
            var job = new ExtractionJob("abc");
            store.Add(job);

            Assert.True(store.TryGet("abc", out var found));
            Assert.Same(job, found);
        }

        [Fact]
        public void Returns_false_for_unknown_or_null_id()
        {
            var store = new InMemoryJobStore();
            Assert.False(store.TryGet("missing", out _));
            Assert.False(store.TryGet(null!, out _));
        }

        [Fact]
        public void Job_tracks_log_count_and_status_transitions()
        {
            var job = new ExtractionJob("j");
            Assert.Equal(JobStatus.Running, job.Status);

            job.AppendLog(1, "hello");
            job.SetProcessedCount(3);
            Assert.Equal(new[] { "    hello" }, job.LogSnapshot);
            Assert.Equal(3, job.ProcessedCount);

            job.Fail("kaboom");
            Assert.Equal(JobStatus.Faulted, job.Status);
            Assert.True(job.IsDone);
            Assert.Equal("kaboom", job.Error);
        }
    }
}
