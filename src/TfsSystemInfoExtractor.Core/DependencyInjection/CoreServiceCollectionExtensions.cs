using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Export;
using TfsSystemInfoExtractor.Core.Extraction;
using TfsSystemInfoExtractor.Core.Reporting;

namespace TfsSystemInfoExtractor.Core.DependencyInjection
{
    public static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the domain/application services: the id parser, hierarchy walker,
        /// extraction use-case, the clock, the JSON result serializer, and the pure
        /// (CSV / Markdown) report renderers. Infrastructure (a real
        /// <see cref="IWorkItemSource"/>, the browser PDF engine, ...) is contributed
        /// separately by the Infrastructure and Web packages.
        /// </summary>
        public static IServiceCollection AddExtractorCore(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<ExtractionOptions>()
                .Bind(configuration.GetSection(ExtractionOptions.SectionName));

            services.TryAddSingleton<ISystemClock, SystemClock>();
            services.AddSingleton<WorkItemIdParser>();
            services.AddScoped<HierarchyWalker>();
            services.AddScoped<ExtractionService>();

            services.AddSingleton<JsonExportFormatter>();

            services.AddSingleton<IReportRenderer, CsvReportRenderer>();
            services.AddSingleton<IReportRenderer, MarkdownReportRenderer>();
            services.AddSingleton<ReportRendererSelector>();

            return services;
        }
    }
}
