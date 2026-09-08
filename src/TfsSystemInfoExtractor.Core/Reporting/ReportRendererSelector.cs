using System;
using System.Collections.Generic;
using System.Linq;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Reporting
{
    /// <summary>Resolves the <see cref="IReportRenderer"/> for the short token the UI sends ("xlsx", "md", "pdf").</summary>
    public sealed class ReportRendererSelector
    {
        private static readonly IReadOnlyDictionary<string, ExportFormat> Tokens =
            new Dictionary<string, ExportFormat>(StringComparer.OrdinalIgnoreCase)
            {
                ["xlsx"] = ExportFormat.Excel,
                ["excel"] = ExportFormat.Excel,
                ["md"] = ExportFormat.Markdown,
                ["markdown"] = ExportFormat.Markdown,
                ["pdf"] = ExportFormat.Pdf
            };

        private readonly IReadOnlyDictionary<ExportFormat, IReportRenderer> _byFormat;

        public ReportRendererSelector(IEnumerable<IReportRenderer> renderers)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            _byFormat = renderers.ToDictionary(r => r.Format);
        }

        public IEnumerable<IReportRenderer> All => _byFormat.Values;

        public bool TryResolve(string? token, out IReportRenderer renderer)
        {
            renderer = null!;
            return token != null
                   && Tokens.TryGetValue(token, out var format)
                   && _byFormat.TryGetValue(format, out renderer);
        }
    }
}
