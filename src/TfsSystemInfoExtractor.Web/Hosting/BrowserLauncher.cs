using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TfsSystemInfoExtractor.Web.Hosting
{
    public interface IBrowserLauncher
    {
        void Open(string url);
    }

    public sealed class BrowserLauncher : IBrowserLauncher
    {
        private readonly ILogger<BrowserLauncher> _logger;

        public BrowserLauncher(ILogger<BrowserLauncher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not open a browser automatically. Open {Url} manually.", url);
            }
        }
    }
}
