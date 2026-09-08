namespace TfsSystemInfoExtractor.Core.Exceptions
{
    /// <summary>
    /// Azure DevOps returned <c>401 Unauthorized</c>. The configured credentials are not
    /// valid (or none were accepted). The run flow catches this to prompt for a sign-in
    /// and retry, rather than failing with a raw error.
    /// </summary>
    public sealed class TfsAuthenticationRequiredException : TfsExtractorException
    {
        public TfsAuthenticationRequiredException(bool credentialsWereSupplied)
            : base(credentialsWereSupplied
                ? "Azure DevOps rejected the supplied username and password (401 Unauthorized)."
                : "Azure DevOps returned 401 Unauthorized - sign in to continue.")
        {
            CredentialsWereSupplied = credentialsWereSupplied;
        }

        /// <summary>True when the 401 came back after an explicit username/password was already tried.</summary>
        public bool CredentialsWereSupplied { get; }
    }
}
