using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

        private static (TfsWorkItemSource source, StubHttpMessageHandler handler) Build(StubHttpMessageHandler handler)
        {
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            var client = new TfsRestClient(new HttpClient(handler), options);
            var catalog = new TfsFieldCatalog(client);
            var resolver = new TfsSystemInfoFieldResolver(catalog, options);
            var source = new TfsWorkItemSource(client, resolver, catalog, new HtmlToPlainTextConverter(), options);
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
    }
}
