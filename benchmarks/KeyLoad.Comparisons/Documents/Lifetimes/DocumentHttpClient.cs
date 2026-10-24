using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class DocumentHttpClient : IDisposable
{
    private readonly SocketsHttpHandler handler;
    internal HttpClient Client
    {
        get;
    }
    internal DocumentHttpClient(HttpClient source, IOptions<ComparisonExecutionOptions> options)
    {
        var settings = ComparisonExecutionOptions.Require(options).Value;
        handler = new()
        {
            MaxConnectionsPerServer = settings.DocumentClientMaxConnections,
            MaxResponseHeadersLength = settings.DocumentClientMaxResponseHeadersKilobytes
        };
        try
        {
            Client = new(handler, disposeHandler: false)
            {
                BaseAddress = source.BaseAddress,
                Timeout = source.Timeout,
                DefaultRequestVersion = source.DefaultRequestVersion,
                DefaultVersionPolicy = source.DefaultVersionPolicy
            };
            foreach (var header in source.DefaultRequestHeaders)
            {
                Client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        catch (Exception) { handler.Dispose(); throw; }
    }
    public void Dispose()
    {
        try
        {
            Client.Dispose();
        }
        finally { handler.Dispose(); }
    }
}
