using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Reporting;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.Export;
using TfsSystemInfoExtractor.Infrastructure.Storage;
using TfsSystemInfoExtractor.Infrastructure.Text;
using TfsSystemInfoExtractor.Infrastructure.Tfs;

namespace TfsSystemInfoExtractor.Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceCollectionExtensions
    {
        /// <summary>
        /// Wires the TFS adapters, the HTML-to-text converter, the file-system artifact
        /// store, and the browser-backed PDF engine.
        /// </summary>
        public static IServiceCollection AddTfsInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<TfsOptions>()
                .Bind(configuration.GetSection(TfsOptions.SectionName));
            services.AddSingleton<IValidateOptions<TfsOptions>, TfsOptionsValidator>();

            services.AddOptions<TfsSourceControlOptions>()
                .Bind(configuration.GetSection(TfsSourceControlOptions.SectionName));

            services.AddOptions<ExportOptions>()
                .Bind(configuration.GetSection(ExportOptions.SectionName));

            // Holds the credential the TFS client authenticates with: the Windows identity
            // by default, or an explicit username/password once a run hits 401 and the
            // user signs in. Singleton so the choice is process-wide and the HTTP handler
            // (which keeps a reference) picks up a change immediately.
            services.AddSingleton<TfsCredentialStore>();
            services.AddSingleton<Core.Abstractions.ITfsCredentialPrompt>(sp => sp.GetRequiredService<TfsCredentialStore>());

            services.AddHttpClient(TfsRestClient.HttpClientName, (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<TfsOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                })
                .ConfigurePrimaryHttpMessageHandler(provider => new HttpClientHandler
                {
                    // an ICredentials the handler consults per auth challenge; returns the
                    // Windows identity until an explicit sign-in replaces it
                    Credentials = provider.GetRequiredService<TfsCredentialStore>(),
                    UseDefaultCredentials = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });

            services.AddScoped(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                return new TfsRestClient(factory.CreateClient(TfsRestClient.HttpClientName),
                    provider.GetRequiredService<IOptions<TfsOptions>>(),
                    provider.GetRequiredService<TfsCredentialStore>());
            });

            services.AddScoped<IFieldCatalog, TfsFieldCatalog>();
            services.AddScoped<ISystemInfoFieldResolver, TfsSystemInfoFieldResolver>();
            services.AddScoped<IWorkItemSourceControlProvider, TfsGitSourceControlProvider>();
            services.AddScoped<IWorkItemSource, TfsWorkItemSource>();
            services.AddSingleton<IHtmlToText, HtmlToPlainTextConverter>();
            services.AddSingleton<IExtractionArtifactStore, FileSystemArtifactStore>();

            services.AddSingleton<IBrowserPdfEngine, EdgeHtmlToPdfEngine>();

            return services;
        }
    }
}
