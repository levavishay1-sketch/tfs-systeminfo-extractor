using System;
using System.Text;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;
using TfsSystemInfoExtractor.Infrastructure.Export;
using TfsSystemInfoExtractor.Tests.Fakes;
using Xunit;

namespace TfsSystemInfoExtractor.Tests.Infrastructure
{
    public class PdfExportFormatterTests
    {
        private readonly PdfExportFormatter _formatter = new PdfExportFormatter(new PdfReportDocumentBuilder());

        private static bool IsPdf(byte[] bytes) =>
            bytes.Length > 400 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

        [Fact]
        public void Produces_a_pdf_artifact_with_the_expected_metadata()
        {
            var result = ResultBuilder.Result(
                ResultBuilder.Node(1, title: "Root", systemInfo: "Browser: Chrome\nOS: Win11").WithChildren(
                    ResultBuilder.Node(2, systemInfo: "Server: WEB03"),
                    ResultBuilder.Node(3, error: "does not exist (404)")));

            var artifact = _formatter.Render(result);

            Assert.Equal(ExportFormat.Pdf, artifact.Format);
            Assert.Equal("application/pdf", artifact.ContentType);
            Assert.Equal("TfsSystemInfo_2026-09-07_120000.pdf", artifact.FileName);
            Assert.True(IsPdf(artifact.Content));
        }

        [Fact]
        public void Renders_an_empty_result_without_throwing()
        {
            var artifact = _formatter.Render(ResultBuilder.Result());
            Assert.True(IsPdf(artifact.Content));
        }

        [Fact]
        public void Renders_deep_hierarchy_source_control_and_non_ascii_system_info()
        {
            var deep = ResultBuilder.Node(1, systemInfo: "שלום עולם");
            var mid = ResultBuilder.Node(2);
            var leaf = ResultBuilder.Node(3, systemInfo: new string('x', 4000));
            mid.WithChildren(leaf);
            deep.WithChildren(mid);
            deep.SourceControl = new SourceControlInfo(
                repositories: new[] { new SourceRepository("r1", "web"), new SourceRepository("r2", "api") },
                commits: new[]
                {
                    new Commit("abc1234567", "Fix login page", new Developer("dev", displayName: "Jane Doe")),
                    new Commit("def8901234", "Bump version")
                },
                contributors: new[] { new Developer("dev", displayName: "Jane Doe") });

            var artifact = _formatter.Render(ResultBuilder.Result(deep));

            Assert.True(IsPdf(artifact.Content));
        }

        [Fact]
        public void Rejects_null_result()
        {
            Assert.Throws<ArgumentNullException>(() => _formatter.Render(null!));
        }
    }
}
