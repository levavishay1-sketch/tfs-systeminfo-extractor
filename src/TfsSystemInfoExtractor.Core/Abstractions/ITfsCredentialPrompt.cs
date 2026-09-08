namespace TfsSystemInfoExtractor.Core.Abstractions
{
    /// <summary>
    /// Receives a username / password the user typed into the sign-in dialog after a run
    /// hit <c>401 Unauthorized</c>. The implementation keeps them in memory only for the
    /// life of the process - they are never persisted or logged.
    /// </summary>
    public interface ITfsCredentialPrompt
    {
        void Set(string userName, string password);
    }
}
