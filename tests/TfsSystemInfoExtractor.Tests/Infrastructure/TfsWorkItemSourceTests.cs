using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Exceptions;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.Text;
using TfsSystemInfoExtractor.Infrastructure.Tfs;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Infrastructure
{
    public class TfsWorkItemSourceTests
    {
        private const string FieldsJson = @"{ ""value"": [
            { ""name"": ""Title"", ""referenceName"": ""System.Title"" },
            { ""name"": ""System Info"", ""referenceName"": ""Custom.SystemInfo"" },
            { ""name"": ""Assigned To"", ""referenceName"": ""System.AssignedTo"", ""type"": ""string"" },
            { ""name"": ""Priority"", ""referenceName"": ""Microsoft.VSTS.Common.Priority"", ""type"": ""integer"" },
            { ""name"": ""Acceptance Criteria"", ""referenceName"": ""Microsoft.VSTS.Common.AcceptanceCriteria"", ""type"": ""html"" } ] }";

        private static readonly TfsOptions Options = new TfsOptions
        {
            CollectionUrl = "http://tfs:8080/tfs/Coll",
            ApiVersion = "3.0",
            SystemInfoFieldDisplayName = "System Info",
            ChildLinkRelation = "System.LinkTypes.Hierarchy-Forward"
        };

        private static (TfsWorkItemSource source, StubHttpMessageHandler handler) Build(
            StubHttpMessageHandler handler, TfsSourceControlOptions? sourceControlOptions = null)
        {
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            var client = new TfsRestClient(new HttpClient(handler), options);
            var catalog = new TfsFieldCatalog(client);
            var resolver = new TfsSystemInfoFieldResolver(catalog, options);
            var scProvider = new TfsGitSourceControlProvider(
                client,
                Microsoft.Extensions.Options.Options.Create(sourceControlOptions ?? new TfsSourceControlOptions()),
                NullLogger<TfsGitSourceControlProvider>.Instance);
            var source = new TfsWorkItemSource(client, resolver, catalog, new HtmlToPlainTextConverter(),
                scProvider, NullLogger<TfsWorkItemSource>.Instance, options);
            return (source, handler);
        }

        [Fact]
        public async Task Maps_fields_web_url_and_child_ids()
        {
            var workItemJson = @"{
                ""id"": 42,
                ""fields"": { ""System.WorkItemType"": ""Bug"", ""System.Title"": ""Login fails"", ""System.State"": ""Active"", ""Custom.SystemInfo"": ""<p>Chrome</p>"" },
                ""_links"": { ""html"": { ""href"": ""http://tfs/web/42"" } },
                ""relations"": [
                    { ""rel"": ""System.LinkTypes.Hierarchy-Forward"", ""url"": ""http://tfs/_apis/wit/workItems/43"" },
                    { ""rel"": ""System.LinkTypes.Hierarchy-Reverse"", ""url"": ""http://tfs/_apis/wit/workItems/1"" },
                    { ""rel"": ""System.LinkTypes.Hierarchy-Forward"", ""url"": ""http://tfs/_apis/wit/workItems/44"" } ] }";

            var (source, _) = Build(new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/42", workItemJson));

            var raw = await source.GetAsync(42, CancellationToken.None);

            Assert.Equal("Bug", raw.Type);
            Assert.Equal("Login fails", raw.Title);
            Assert.Equal("http://tfs/web/42", raw.WebUrl);
            Assert.Equal("<p>Chrome</p>", raw.SystemInfoHtml);
            Assert.Equal(new[] { 43, 44 }, raw.ChildIds);
        }

        [Fact]
        public async Task Extracts_extra_fields_flattening_identities_and_stripping_html()
        {
            var workItemJson = @"{
                ""id"": 5,
                ""fields"": {
                    ""System.WorkItemType"": ""Bug"",
                    ""System.Title"": ""t"",
                    ""System.State"": ""Active"",
                    ""Custom.SystemInfo"": ""<p>ignored</p>"",
                    ""System.AssignedTo"": { ""displayName"": ""Jane Doe"", ""uniqueName"": ""jane@corp"" },
                    ""Microsoft.VSTS.Common.Priority"": 2,
                    ""Microsoft.VSTS.Common.AcceptanceCriteria"": ""<div>Given <b>x</b></div><div>Then y</div>"",
                    ""System.Tags"": """"
                },
                ""relations"": [] }";

            var (source, _) = Build(new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/5", workItemJson));

            var raw = await source.GetAsync(5, CancellationToken.None);

            Assert.NotNull(raw.Fields);
            Assert.Equal("Jane Doe", raw.Fields!["System.AssignedTo"]);
            Assert.Equal("2", raw.Fields["Microsoft.VSTS.Common.Priority"]);
            Assert.Equal("Given x\nThen y", raw.Fields["Microsoft.VSTS.Common.AcceptanceCriteria"]);
            Assert.False(raw.Fields.ContainsKey("System.Title"));       // default column
            Assert.False(raw.Fields.ContainsKey("Custom.SystemInfo"));  // System Info
            Assert.False(raw.Fields.ContainsKey("System.Tags"));        // empty value dropped
        }

        [Fact]
        public async Task Translates_404_to_a_not_found_access_exception()
        {
            var (source, _) = Build(new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/99", "{}", HttpStatusCode.NotFound));

            var ex = await Assert.ThrowsAsync<WorkItemAccessException>(() => source.GetAsync(99, CancellationToken.None));
            Assert.Equal(WorkItemAccessError.NotFound, ex.Error);
        }

        [Fact]
        public async Task Translates_403_to_a_forbidden_access_exception()
        {
            var (source, _) = Build(new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/7", "denied", HttpStatusCode.Forbidden));

            var ex = await Assert.ThrowsAsync<WorkItemAccessException>(() => source.GetAsync(7, CancellationToken.None));
            Assert.Equal(WorkItemAccessError.Forbidden, ex.Error);
        }

        [Fact]
        public async Task Missing_system_info_field_is_reported()
        {
            var (source, _) = Build(new StubHttpMessageHandler()
                .Map("_apis/wit/fields", @"{ ""value"": [ { ""name"": ""Title"", ""referenceName"": ""System.Title"" } ] }"));

            await Assert.ThrowsAsync<SystemInfoFieldNotFoundException>(() => source.GetAsync(1, CancellationToken.None));
        }

        [Fact]
        public async Task Field_catalogue_is_only_fetched_once()
        {
            var handler = new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/", @"{ ""id"": 1, ""fields"": {}, ""relations"": [] }");
            var (source, _) = Build(handler);

            await source.GetAsync(1, CancellationToken.None);
            await source.GetAsync(2, CancellationToken.None);

            Assert.Single(handler.RequestedUrls.FindAll(u => u.Contains("_apis/wit/fields")));
        }

        private const string CommitLinkWorkItem = @"{
            ""id"": 60, ""fields"": { ""System.WorkItemType"": ""Feature"", ""System.Title"": ""t"", ""System.State"": ""Active"" },
            ""relations"": [
                { ""rel"": ""ArtifactLink"", ""attributes"": { ""name"": ""Fixed in Commit"" },
                  ""url"": ""vstfs:///Git/Commit/a11e1111-1111-1111-1111-111111111111%2Fb22e2222-2222-2222-2222-222222222222%2F1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b"" },
                { ""rel"": ""System.LinkTypes.Hierarchy-Reverse"", ""url"": ""http://tfs/_apis/wit/workItems/1"" } ] }";

        [Fact]
        public async Task Resolves_a_linked_git_commit_into_source_control_info()
        {
            var handler = new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/60", CommitLinkWorkItem)
                .Map("_apis/git/repositories/b22e2222-2222-2222-2222-222222222222/commits/1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b/changes",
                    @"{ ""changes"": [
                        { ""item"": { ""path"": ""/Payments/Api/PayService.cs"", ""gitObjectType"": ""blob"" } },
                        { ""item"": { ""path"": ""/Reporting/Report.cs"", ""gitObjectType"": ""blob"" } },
                        { ""item"": { ""path"": ""/Payments/Model.cs"", ""gitObjectType"": ""blob"" } } ] }")
                .Map("_apis/git/repositories/b22e2222-2222-2222-2222-222222222222/commits/1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b",
                    @"{ ""commitId"": ""1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b"",
                        ""comment"": ""Wire up the payment service"",
                        ""author"": { ""name"": ""Dana Cohen"", ""email"": ""dcohen@corp"", ""date"": ""2026-09-05T14:30:00Z"" },
                        ""committer"": { ""name"": ""Dana Cohen"", ""email"": ""dcohen@corp"", ""date"": ""2026-09-05T14:31:00Z"" },
                        ""remoteUrl"": ""http://tfs/_git/Reports/commit/1a2b3c4d"" }")
                .Map("_apis/git/repositories/b22e2222-2222-2222-2222-222222222222",
                    @"{ ""id"": ""b22e2222-2222-2222-2222-222222222222"", ""name"": ""Reports.Web"",
                        ""project"": { ""name"": ""Reports"" }, ""remoteUrl"": ""http://tfs/_git/Reports.Web"" }");
            var (source, _) = Build(handler);

            var raw = await source.GetAsync(60, CancellationToken.None);

            Assert.NotNull(raw.SourceControl);
            var sc = raw.SourceControl!;

            var repo = Assert.Single(sc.Repositories);
            Assert.Equal("Reports.Web", repo.Name);
            Assert.Equal("TfsGit", repo.Kind);

            var commit = Assert.Single(sc.Commits);
            Assert.Equal("1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b", commit.Id);
            Assert.Equal("1a2b3c4d", commit.ShortId);
            Assert.Equal("Wire up the payment service", commit.Message);
            Assert.Equal("Dana Cohen", commit.Author!.DisplayName);
            Assert.Equal("dcohen@corp", commit.Author.UniqueName);
            Assert.Equal(2026, commit.CommittedOn!.Value.Year);
            Assert.Equal("http://tfs/_git/Reports/commit/1a2b3c4d", commit.Url);
            Assert.Equal(new[] { "Payments", "Reporting" }, commit.Components);

            Assert.Equal(new[] { "Payments", "Reporting" }, sc.Components.Select(c => c.Name).ToArray());
            Assert.Equal("Dana Cohen", Assert.Single(sc.Contributors).DisplayName);
        }

        [Fact]
        public async Task A_commit_whose_detail_cannot_be_read_is_skipped_not_thrown()
        {
            var handler = new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/60", CommitLinkWorkItem)
                .Map("_apis/git/repositories/b22e2222-2222-2222-2222-222222222222/commits/1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b", "gone", HttpStatusCode.NotFound)
                .Map("_apis/git/repositories/b22e2222-2222-2222-2222-222222222222",
                    @"{ ""id"": ""b22e2222-2222-2222-2222-222222222222"", ""name"": ""Reports.Web"" }");
            var (source, _) = Build(handler);

            var raw = await source.GetAsync(60, CancellationToken.None);

            // the repo still resolved; the unreadable commit is simply absent
            Assert.NotNull(raw.SourceControl);
            Assert.Empty(raw.SourceControl!.Commits);
            Assert.Single(raw.SourceControl.Repositories);
        }

        [Fact]
        public async Task Source_control_is_not_touched_when_disabled()
        {
            var handler = new StubHttpMessageHandler()
                .Map("_apis/wit/fields", FieldsJson)
                .Map("_apis/wit/workitems/60", CommitLinkWorkItem);
            var (source, _) = Build(handler, new TfsSourceControlOptions { Enabled = false });

            var raw = await source.GetAsync(60, CancellationToken.None);

            Assert.Null(raw.SourceControl);
            Assert.DoesNotContain(handler.RequestedUrls, u => u.Contains("_apis/git/"));
        }
    }
}
