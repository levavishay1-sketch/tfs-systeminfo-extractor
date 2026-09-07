using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Storage
{
    /// <summary>Writes each rendered export into the configured output directory, preserving the artifact's bytes verbatim.</summary>
    public sealed class FileSystemArtifactStore : IExtractionArtifactStore
    {
        private readonly ExportOptions _options;
        private readonly ILogger<FileSystemArtifactStore> _logger;

        public FileSystemArtifactStore(IOptions<ExportOptions> options, ILogger<FileSystemArtifactStore> logger)
        {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> SaveAsync(ExportArtifact artifact, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_options.OutputDirectory);
            var path = Path.Combine(_options.OutputDirectory, artifact.FileName);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true))
            {
                await stream.WriteAsync(artifact.Content, 0, artifact.Content.Length, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Wrote {Format} export to {Path}", artifact.Format, path);
            return path;
        }
    }
}
