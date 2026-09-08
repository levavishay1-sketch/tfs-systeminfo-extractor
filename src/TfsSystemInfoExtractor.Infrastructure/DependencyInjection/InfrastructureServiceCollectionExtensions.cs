using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.Export;
using TfsSystemInfoExtractor.Infrastructure.Storage;
using TfsSystemInfoExtractor.Infrastructure.Text;
using TfsSystemInfoExtractor.Infrastructure.Tfs;

namespace TfsSystemInfoExtractor.Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceCollectionExtensions
    {
        /// <summary>Wires the TFS adapters, the HTML-to-text converter and the file-system artifact store.</summary>
        public static IServiceCollection AddTfsInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<TfsOptions>()
                .Bind(configuration.GetSection(TfsOptions.SectionName));
            services.AddSingleton<IValidateOptions<TfsOptions>, TfsOptionsValidator>();

            services.AddOptions<ExportOptions>()
                .Bind(configuration.GetSection(ExportOptions.SectionName));

            services.AddHttpClient(TfsRestClient.HttpClientName, (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<TfsOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    UseDefaultCredentials = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });

            services.AddScoped(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                return new TfsRestClient(factory.CreateClient(TfsRestClient.HttpClientName),
                    provider.GetRequiredService<IOptions<TfsOptions>>());
            });

            services.AddScoped<IFieldCatalog, TfsFieldCatalog>();
            services.AddScoped<ISystemInfoFieldResolver, TfsSystemInfoFieldResolver>();
            services.AddScoped<IWorkItemSource, TfsWorkItemSource>();
            services.AddSingleton<IHtmlToText, HtmlToPlainTextConverter>();
            services.AddSingleton<IExtractionArtifactStore, FileSystemArtifactStore>();

            // Output adapter with a heavy third-party (PDF) dependency - kept out of Core,
            // wired through the same IExportFormatter port as the built-in formatters.
            services.AddSingleton<PdfReportDocumentBuilder>();
            services.AddSingleton<IExportFormatter, PdfExportFormatter>();

            return services;
        }
    }
}
