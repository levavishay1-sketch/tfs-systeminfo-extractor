using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.DependencyInjection;
using TfsSystemInfoExtractor.Infrastructure.Configuration;
using TfsSystemInfoExtractor.Infrastructure.DependencyInjection;
using TfsSystemInfoExtractor.Web.DependencyInjection;
using TfsSystemInfoExtractor.Web.Hosting;

namespace TfsSystemInfoExtractor.App
{
    /// <summary>
    /// Composition root only: build configuration, assemble the DI container from each
    /// layer's registration extension, validate options, then run the local web host
    /// until Ctrl+C. All behaviour lives in the Core / Infrastructure / Web libraries.
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"), optional: true, reloadOnChange: false)
                .AddEnvironmentVariables("TFS_")
                .Build();

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddSimpleConsole(o => o.SingleLine = true);
            });

            services.AddExtractorCore(configuration);
            services.AddTfsInfrastructure(configuration);
            services.AddWebUi(configuration);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

            try
            {
                var tfs = provider.GetRequiredService<IOptions<TfsOptions>>().Value; // triggers validation
                logger.LogInformation("TFS collection: {CollectionUrl}", tfs.CollectionUrl);
            }
            catch (OptionsValidationException ex)
            {
                foreach (var failure in ex.Failures)
                {
                    logger.LogError("Configuration error: {Failure}", failure);
                }

                return 1;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            logger.LogInformation("Press Ctrl+C to stop.");
            try
            {
                await provider.GetRequiredService<LocalHttpServer>().RunAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on Ctrl+C
            }

            return 0;
        }
    }
}
