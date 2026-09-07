using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfsSystemInfoExtractor.Core.DependencyInjection;
using TfsSystemInfoExtractor.Core.Extraction;
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
    }
}
