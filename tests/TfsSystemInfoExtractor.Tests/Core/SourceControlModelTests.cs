using System;
using System.Text;
using System.Text.Json;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Model.SourceControl;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Core
{
    public class SourceControlModelTests
    {
        [Fact]
        public void Empty_info_has_empty_collections_and_reports_empty()
        {
            var info = SourceControlInfo.Empty;

            Assert.Empty(info.Repositories);
            Assert.Empty(info.Commits);
            Assert.Empty(info.Contributors);
            Assert.True(info.IsEmpty);
        }

        [Fact]
        public void Info_holds_multiple_repositories_commits_contributors_and_components()
        {
            var jane = new Developer("DOMAIN\\jdoe", "jane@corp", "Jane Doe");
            var info = new SourceControlInfo(
                repositories: new[]
                {
                    new SourceRepository("r1", "web", "http://tfs/_git/web", "TfsGit"),
                    new SourceRepository("r2", "api")
                },
                commits: new[]
                {
                    new Commit("abcdef1234567890", "Fix login", jane, DateTimeOffset.UtcNow, "r1", "http://tfs/commit/abc",
                        components: new[] { "Auth", "Auth", "Web" },
                        changedPaths: new[] { "/Components/Auth/Login.cs", "/Components/Web/Nav.cs" }),
                    new Commit("00998877", "Bump version", jane, null, "r2")
                },
                contributors: new[] { jane },
                components: new[] { new SourceComponent("Auth", "r1"), new SourceComponent("Web", "r1") });

            Assert.Equal(2, info.Repositories.Count);
            Assert.Equal(2, info.Commits.Count);
            Assert.Single(info.Contributors);
            Assert.Equal(2, info.Components.Count);
            Assert.False(info.IsEmpty);
            Assert.Equal("abcdef12", info.Commits[0].ShortId);
            Assert.Equal("00998877", info.Commits[1].ShortId);
            Assert.Equal(new[] { "Auth", "Web" }, info.Commits[0].Components);   // de-duplicated
            Assert.Equal(new[] { "/Components/Auth/Login.cs", "/Components/Web/Nav.cs" }, info.Commits[0].ChangedPaths);
            Assert.Empty(info.Commits[1].Components);
            Assert.Empty(info.Commits[1].ChangedPaths);
            Assert.Equal("Jane Doe", info.Contributors[0].DisplayName);
        }

        [Fact]
        public void Component_requires_a_name()
        {
            Assert.Throws<ArgumentException>(() => new SourceComponent("  "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_developer_name_is_rejected(string? name)
        {
            Assert.Throws<ArgumentException>(() => new Developer(name!));
        }

        [Fact]
        public void Blank_commit_id_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => new Commit(""));
        }

        [Fact]
        public void Repository_name_falls_back_to_id_when_missing()
        {
            Assert.Equal("r7", new SourceRepository("r7", "").Name);
        }

        [Fact]
        public void Json_export_includes_source_control_when_present_and_omits_it_when_null()
        {
            var withScNode = ResultBuilder.Node(1, systemInfo: "x");
            withScNode.SourceControl = new SourceControlInfo(
                repositories: new[] { new SourceRepository("r1", "Reports.Web", null, "TfsGit") },
                commits: new[] { new Commit("deadbeef", "msg", new Developer("dev"), null, "r1", null, new[] { "Auth" }, new[] { "/Components/Auth/x.cs" }) },
                contributors: new[] { new Developer("dev") },
                components: new[] { new SourceComponent("Auth", "r1") });
            var plainNode = ResultBuilder.Node(2);

            var json = Encoding.UTF8.GetString(new JsonExportFormatter().Render(ResultBuilder.Result(withScNode, plainNode)).Content);
            using var doc = JsonDocument.Parse(json);
            var roots = doc.RootElement.GetProperty("Roots");

            Assert.True(roots[0].TryGetProperty("SourceControl", out var sc));
            Assert.Equal("deadbeef", sc.GetProperty("Commits")[0].GetProperty("Id").GetString());
            Assert.Equal("Auth", sc.GetProperty("Commits")[0].GetProperty("Components")[0].GetString());
            Assert.Equal("/Components/Auth/x.cs", sc.GetProperty("Commits")[0].GetProperty("ChangedPaths")[0].GetString());
            Assert.Equal("Reports.Web", sc.GetProperty("Repositories")[0].GetProperty("Name").GetString());
            Assert.Equal("Auth", sc.GetProperty("Components")[0].GetProperty("Name").GetString());
            Assert.Equal("dev", sc.GetProperty("Contributors")[0].GetProperty("Name").GetString());
            Assert.False(roots[1].TryGetProperty("SourceControl", out _));
        }
    }
}
