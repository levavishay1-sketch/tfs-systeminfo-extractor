using System;
using System.Net;
using TfsSystemInfoExtractor.Core.Abstractions;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// The credential the TFS <see cref="System.Net.Http.HttpClient"/> authenticates with.
    /// It is an <see cref="ICredentials"/> the handler consults on every auth challenge,
    /// so switching from the logged-in Windows identity to an explicit username/password
    /// (supplied when a run hits 401) takes effect immediately, with no handler rebuild.
    /// <para>The password lives only in this in-memory instance for the life of the
    /// process - it is never written to configuration, disk or logs.</para>
    /// </summary>
    public sealed class TfsCredentialStore : ICredentials, ITfsCredentialPrompt
    {
        private readonly object _gate = new object();
        private NetworkCredential? _explicit;
        private bool _explicitRejected;

        /// <summary>True once <see cref="Set"/> has been called (an explicit sign-in was provided).</summary>
        public bool HasExplicitCredentials
        {
            get { lock (_gate) { return _explicit != null; } }
        }

        /// <summary>True when the explicit credentials were themselves answered with a 401.</summary>
        public bool ExplicitCredentialsRejected
        {
            get { lock (_gate) { return _explicitRejected; } }
        }

        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            lock (_gate)
            {
                // the sentinel that tells the handler to use the process's Windows identity
                return _explicit ?? CredentialCache.DefaultNetworkCredentials;
            }
        }

        /// <summary>Switch to an explicit username / password. Accepts <c>DOMAIN\user</c> or a bare user name.</summary>
        public void Set(string userName, string password)
        {
            var user = (userName ?? string.Empty).Trim();
            var domain = string.Empty;

            var slash = user.IndexOf('\\');
            if (slash < 0)
            {
                slash = user.IndexOf('/');
            }

            if (slash > 0)
            {
                domain = user.Substring(0, slash);
                user = user.Substring(slash + 1);
            }

            lock (_gate)
            {
                _explicit = new NetworkCredential(user, password ?? string.Empty, domain);
                _explicitRejected = false;
            }
        }

        /// <summary>Record that the current credential was answered with a 401.</summary>
        public void MarkRejected()
        {
            lock (_gate)
            {
                if (_explicit != null)
                {
                    _explicitRejected = true;
                }
            }
        }

        /// <summary>Drop any explicit credential and fall back to the Windows identity.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _explicit = null;
                _explicitRejected = false;
            }
        }
    }
}
