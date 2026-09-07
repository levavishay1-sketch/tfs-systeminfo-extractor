using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TfsSystemInfoExtractor.Web.Http
{
    /// <summary>One handled route. The router asks each endpoint whether it matches, then delegates.</summary>
    public interface IHttpEndpoint
    {
        bool Matches(string httpMethod, string path);

        Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken);
    }
}
