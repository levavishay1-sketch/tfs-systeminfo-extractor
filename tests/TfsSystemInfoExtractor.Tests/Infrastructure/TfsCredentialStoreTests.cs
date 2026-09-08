using System;
using System.Net;
using TfsSystemInfoExtractor.Infrastructure.Tfs;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Infrastructure
{
    public class TfsCredentialStoreTests
    {
        private static readonly Uri Any = new Uri("http://tfs:8080/tfs/Coll");

        [Fact]
        public void Uses_the_windows_identity_until_an_explicit_sign_in()
        {
            var store = new TfsCredentialStore();

            Assert.False(store.HasExplicitCredentials);
            Assert.Same(CredentialCache.DefaultNetworkCredentials, store.GetCredential(Any, "Negotiate"));
        }

        [Theory]
        [InlineData("CORP\\jdoe", "CORP", "jdoe")]
        [InlineData("CORP/jdoe", "CORP", "jdoe")]
        [InlineData("jdoe", "", "jdoe")]
        [InlineData("  jdoe@corp.com  ", "", "jdoe@corp.com")]
        public void Set_parses_the_domain_out_of_the_user_name(string input, string domain, string user)
        {
            var store = new TfsCredentialStore();

            store.Set(input, "secret");

            var cred = store.GetCredential(Any, "NTLM")!;
            Assert.Equal(user, cred.UserName);
            Assert.Equal(domain, cred.Domain);
            Assert.Equal("secret", cred.Password);
            Assert.True(store.HasExplicitCredentials);
        }

        [Fact]
        public void Rejected_flag_only_applies_to_explicit_credentials()
        {
            var store = new TfsCredentialStore();

            store.MarkRejected();
            Assert.False(store.ExplicitCredentialsRejected); // Windows identity - not "rejected credentials"

            store.Set("jdoe", "p");
            store.MarkRejected();
            Assert.True(store.ExplicitCredentialsRejected);

            store.Set("jdoe", "p2"); // a fresh sign-in clears the flag
            Assert.False(store.ExplicitCredentialsRejected);
        }

        [Fact]
        public void Reset_returns_to_the_windows_identity()
        {
            var store = new TfsCredentialStore();
            store.Set("jdoe", "p");

            store.Reset();

            Assert.False(store.HasExplicitCredentials);
            Assert.Same(CredentialCache.DefaultNetworkCredentials, store.GetCredential(Any, "Negotiate"));
        }
    }
}
