using System;
using System.Collections.Generic;
using System.Linq;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;

namespace TfsSystemInfoExtractor.Core.Export
{
    /// <summary>
    /// Looks up the registered <see cref="IExportFormatter"/> for a format, by enum
    /// or by the short token used in the UI query string ("json", "md", "csv").
    /// </summary>
    public sealed class ExportFormatterSelector
    {
        private readonly IReadOnlyDictionary<ExportFormat, IExportFormatter> _byFormat;

        private static readonly IReadOnlyDictionary<string, ExportFormat> Tokens =
            new Dictionary<string, ExportFormat>(StringComparer.OrdinalIgnoreCase)
            {
                ["json"] = ExportFormat.Json,
                ["md"] = ExportFormat.Markdown,
                ["markdown"] = ExportFormat.Markdown,
                ["csv"] = ExportFormat.Csv
            };

        public ExportFormatterSelector(IEnumerable<IExportFormatter> formatters)
        {
            if (formatters == null) throw new ArgumentNullException(nameof(formatters));
            _byFormat = formatters.ToDictionary(f => f.Format);
        }

        public IEnumerable<IExportFormatter> All => _byFormat.Values;

        public bool TryResolve(string? token, out IExportFormatter formatter)
        {
            formatter = null!;
            return token != null
                   && Tokens.TryGetValue(token, out var format)
                   && _byFormat.TryGetValue(format, out formatter);
        }

        public IExportFormatter Get(ExportFormat format) => _byFormat[format];
    }
}
