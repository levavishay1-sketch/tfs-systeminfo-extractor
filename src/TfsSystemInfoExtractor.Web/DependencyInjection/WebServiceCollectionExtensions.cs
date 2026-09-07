using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TfsSystemInfoExtractor.Web.Endpoints;
using TfsSystemInfoExtractor.Web.Hosting;
using TfsSystemInfoExtractor.Web.Http;
using TfsSystemInfoExtractor.Web.Jobs;
using TfsSystemInfoExtractor.Web.Ui;

namespace TfsSystemInfoExtractor.Web.DependencyInjection
{
    public static class WebServiceCollectionExtensions
    {
        /// <summary>Registers the local web UI: the host, router, endpoints, job management and embedded assets.</summary>
        public static IServiceCollection AddWebUi(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<WebOptions>()
                .Bind(configuration.GetSection(WebOptions.SectionName));

            services.AddSingleton<IUiAssetProvider, EmbeddedUiAssetProvider>();
            services.AddSingleton<IBrowserLauncher, BrowserLauncher>();

            services.AddSingleton<IJobStore, InMemoryJobStore>();
            services.AddSingleton<IJobManager, JobManager>();

            services.AddSingleton<IHttpEndpoint, IndexEndpoint>();
            services.AddSingleton<IHttpEndpoint, StartExtractionEndpoint>();
            services.AddSingleton<IHttpEndpoint, JobStatusEndpoint>();
            services.AddSingleton<IHttpEndpoint, DownloadArtifactEndpoint>();
            services.AddSingleton<RequestRouter>();

            services.AddSingleton<LocalHttpServer>();

            return services;
        }
    }
}
