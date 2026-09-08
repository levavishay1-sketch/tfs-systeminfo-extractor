using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfsSystemInfoExtractor.Core.DependencyInjection;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Infrastructure.DependencyInjection;
using TfsSystemInfoExtractor.Web.DependencyInjection;
using TfsSystemInfoExtractor.Web.Hosting;
using TfsSystemInfoExtractor.Web.Jobs;
using Xunit;

namespace TfsSystemInfoExtractor.Tests
{
    /// <summary>Guards the composition root: every layer's registrations must build and resolve together.</summary>
    public class CompositionTests
    {
        private static ServiceProvider BuildContainer()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tfs:CollectionUrl"] = "http://tfs:8080/tfs/Coll",
                    ["Web:OpenBrowserOnStart"] = "false"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddExtractorCore(configuration);
            services.AddTfsInfrastructure(configuration);
            services.AddWebUi(configuration);

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }

        [Fact]
        public void Container_builds_and_resolves_the_host()
        {
            using var provider = BuildContainer();
            Assert.NotNull(provider.GetRequiredService<LocalHttpServer>());
            Assert.NotNull(provider.GetRequiredService<IJobManager>());
            Assert.NotNull(provider.GetRequiredService<WorkItemIdParser>());
        }

        [Fact]
        public void Per_run_services_resolve_inside_a_scope()
        {
            using var provider = BuildContainer();
            using var scope = provider.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExtractionService>());
        }

        [Fact]
        public void Every_export_renderer_including_the_html_pdf_renderer_is_wired_through_the_selector()
        {
            using var provider = BuildContainer();
            var selector = provider.GetRequiredService<ReportRendererSelector>();

            foreach (var token in new[] { "csv", "md", "markdown", "pdf" })
            {
                Assert.True(selector.TryResolve(token, out _), $"'{token}' did not resolve");
            }

            Assert.True(selector.TryResolve("pdf", out var pdf));
            Assert.Equal(ExportFormat.Pdf, pdf.Format);
        }

        [Fact]
        public void Json_result_serializer_resolves()
        {
            using var provider = BuildContainer();
            Assert.NotNull(provider.GetRequiredService<JsonExportFormatter>());
        }
    }
}
