using System;
using System.IO;
using System.Reflection;

namespace TfsSystemInfoExtractor.Web.Ui
{
    public interface IUiAssetProvider
    {
        /// <summary>The single-page UI document, UTF-8 encoded.</summary>
        byte[] IndexHtml { get; }
    }

    /// <summary>Serves the browser UI from an embedded resource, read once and cached.</summary>
    public sealed class EmbeddedUiAssetProvider : IUiAssetProvider
    {
        private const string ResourceName = "TfsSystemInfoExtractor.Web.Ui.Assets.index.html";

        private readonly Lazy<byte[]> _indexHtml = new Lazy<byte[]>(LoadIndexHtml);

        public byte[] IndexHtml => _indexHtml.Value;

        private static byte[] LoadIndexHtml()
        {
            var assembly = typeof(EmbeddedUiAssetProvider).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded UI resource '{ResourceName}' was not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
