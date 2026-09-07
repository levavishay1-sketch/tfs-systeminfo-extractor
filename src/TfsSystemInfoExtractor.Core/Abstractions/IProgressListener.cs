namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// Receives human-readable progress messages while an extraction runs.
    /// <paramref name="depth"/> is the hierarchy depth, used purely for indentation.
    /// </summary>
    public interface IProgressListener
    {
        void Report(int depth, string message);

        /// <summary>Called once per distinct work item after it has been fetched (or has failed).</summary>
        void ItemProcessed(int totalProcessed);
    }

    /// <summary>A listener that discards every message; handy for tests and non-interactive runs.</summary>
    public sealed class NullProgressListener : IProgressListener
    {
        public static readonly NullProgressListener Instance = new NullProgressListener();

        private NullProgressListener()
        {
        }

        public void Report(int depth, string message)
        {
        }

        public void ItemProcessed(int totalProcessed)
        {
        }
    }
}
