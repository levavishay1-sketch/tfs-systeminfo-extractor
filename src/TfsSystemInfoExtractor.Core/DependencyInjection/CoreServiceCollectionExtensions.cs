using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Extraction;

namespace TfsSystemInfoExtractor.Core.DependencyInjection
{
    public static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the domain/application services: the id parser, hierarchy walker,
        /// extraction use-case, the clock and every export formatter. Infrastructure
        /// (a real <see cref="IWorkItemSource"/>, <see cref="IHtmlToText"/>, etc.) is
        /// contributed separately by the Infrastructure package.
        /// </summary>
        public static IServiceCollection AddExtractorCore(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<ExtractionOptions>()
                .Bind(configuration.GetSection(ExtractionOptions.SectionName));

            services.TryAddSingleton<ISystemClock, SystemClock>();
            services.AddSingleton<WorkItemIdParser>();
            services.AddScoped<HierarchyWalker>();
            services.AddScoped<ExtractionService>();

            services.AddSingleton<IExportFormatter, JsonExportFormatter>();
            services.AddSingleton<IExportFormatter, MarkdownExportFormatter>();
            services.AddSingleton<IExportFormatter, CsvExportFormatter>();
            services.AddSingleton<ExportFormatterSelector>();

            return services;
        }
    }
}
