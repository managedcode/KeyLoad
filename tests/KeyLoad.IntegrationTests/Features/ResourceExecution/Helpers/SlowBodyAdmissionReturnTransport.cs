using System.Net;
using System.Net.Sockets;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed class SlowBodyAdmissionReturnTransport : IDisposable
{
    private const string SecondSocket = "The slow-body attempt unexpectedly opened a second socket.";
    private const string MissingSocket = "No original native socket was connected.";
    private readonly SocketsHttpHandler handler;
    private Socket? socket;
    internal HttpClient Client
    {
        get;
    }
    internal SlowBodyAdmissionReturnTransport(Uri endpoint)
    {
        handler = new SocketsHttpHandler
        {
            UseProxy = false,
            MaxConnectionsPerServer = SlowBodyAdmissionReturnProtocol.OneConnection,
            ConnectCallback = ConnectAsync
        };
        try
        {
            Client = new HttpClient(handler, disposeHandler: false);
            ConfigureClient(Client, endpoint);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(() => Client?.Dispose(), failures);
            ServerFailureObserver.Observe(handler.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
    private static void ConfigureClient(HttpClient client, Uri endpoint)
    {
        client.BaseAddress = endpoint;
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var candidate = new Socket(SocketType.Stream, ProtocolType.Tcp);
        if (Interlocked.CompareExchange(ref socket, candidate, null) is not null)
        {
            candidate.Dispose();
            throw new InvalidOperationException(SecondSocket);
        }
        await candidate.ConnectAsync(context.DnsEndPoint, token);
        return new NetworkStream(candidate, ownsSocket: false);
    }
    internal void Disconnect(bool requireConnected = true)
    {
        if (socket is { } original)
        {
            original.Dispose();
        }
        else if (requireConnected)
        {
            throw new InvalidOperationException(MissingSocket);
        }
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Client.Dispose, failures);
        try
        {
            handler.Dispose();
        }
        catch (Exception original)
        {
            failures.Add(original);
            CompleteDispose(failures);
            throw;
        }
        CompleteDispose(failures);
    }

    private void CompleteDispose(List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => socket?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
