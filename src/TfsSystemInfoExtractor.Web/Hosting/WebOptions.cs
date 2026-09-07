namespace TfsSystemInfoExtractor.Web.Hosting
{
    public sealed class WebOptions
    {
        public const string SectionName = "Web";

        /// <summary>Loopback port the local UI listens on.</summary>
        public int Port { get; set; } = 5050;

        /// <summary>Open the default browser at the UI when the process starts.</summary>
        public bool OpenBrowserOnStart { get; set; } = true;

        public string ListenerPrefix => $"http://localhost:{Port}/";
    }
}
