using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Web.Http;

namespace TfsSystemInfoExtractor.Web.Hosting
{
    /// <summary>
    /// The presentation host: a loopback <see cref="HttpListener"/> accept loop that
    /// hands every request to the <see cref="RequestRouter"/>. Runs until the supplied
    /// token is cancelled.
    /// </summary>
    public sealed class LocalHttpServer
    {
        private readonly RequestRouter _router;
        private readonly IBrowserLauncher _browser;
        private readonly WebOptions _options;
        private readonly ILogger<LocalHttpServer> _logger;

        public LocalHttpServer(
            RequestRouter router,
            IBrowserLauncher browser,
            IOptions<WebOptions> options,
            ILogger<LocalHttpServer> logger)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _browser = browser ?? throw new ArgumentNullException(nameof(browser));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var prefix = _options.ListenerPrefix;
            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            _logger.LogInformation("Local UI listening on {Prefix}", prefix);

            using var registration = cancellationToken.Register(() =>
            {
                try { listener.Stop(); } catch { /* already stopped */ }
            });

            if (_options.OpenBrowserOnStart)
            {
                _browser.Open(prefix);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException ex)
                {
                    _logger.LogWarning(ex, "Listener error; stopping accept loop.");
                    break;
                }

                _ = Task.Run(() => _router.DispatchAsync(context, cancellationToken), CancellationToken.None);
            }

            _logger.LogInformation("Local UI stopped.");
        }
    }
}
