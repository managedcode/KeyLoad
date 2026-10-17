using System.Net.Security;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class PostgresRf3WireOwner : IAsyncDisposable
{
    private readonly TcpClient socket = new();
    private NetworkStream? network;
    private SslStream? tls;
    private int disposed;
    private int receiverAborted;
    private int socketDisposed;
    internal bool ReceiverAborted => Volatile.Read(ref receiverAborted) != PostgresRf3Protocol.FirstColumn;

    internal void AbortReceiver()
    {
        if (Interlocked.Exchange(ref receiverAborted, PostgresRf3Protocol.OneColumn) != PostgresRf3Protocol.FirstColumn)
        {
            return;
        }
        socket.LingerState = new LingerOption(true, PostgresRf3Protocol.FirstColumn);
        if (Interlocked.Exchange(ref socketDisposed, PostgresRf3Protocol.OneColumn) == PostgresRf3Protocol.FirstColumn)
        {
            socket.Dispose();
        }
    }
    internal SslStream Stream => tls ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);

    private PostgresRf3WireOwner()
    {
    }

    internal static async Task<PostgresRf3WireOwner> OpenAsync(ClusterFixture fixture, CancellationToken token)
    {
        var owner = new PostgresRf3WireOwner();
        try
        {
            var endpoint = fixture.App.GetEndpoint(McpCallerProtocol.Node3, PostgresRf3Protocol.Endpoint);
            await owner.InitializeAsync(fixture, endpoint, token).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private async Task InitializeAsync(ClusterFixture fixture, Uri endpoint, CancellationToken token)
    {
        socket.ReceiveBufferSize = PostgresRf3BackpressureProtocol.ReceiveBytes;
        await global::TUnit.Assertions.Assert.That(socket.ReceiveBufferSize).IsLessThanOrEqualTo(PostgresRf3BackpressureProtocol.MaximumEffectiveReceiveBytes);
        await socket.ConnectAsync(endpoint.Host, endpoint.Port, token).ConfigureAwait(false);
        network = socket.GetStream();
        await network.WriteAsync(PostgresRf3WireFrames.SslRequest(), token).ConfigureAwait(false);
        var accepted = new byte[PostgresRf3Protocol.OneColumn];
        await network.ReadExactlyAsync(accepted, token).ConfigureAwait(false);
        await global::TUnit.Assertions.Assert.That(accepted[PostgresRf3Protocol.FirstColumn]).IsEqualTo(PostgresRf3WireFrames.SslAccepted);
        tls = new SslStream(network, leaveInnerStreamOpen: true);
        await PostgresRf3WireTrust.AuthenticateAsync(tls, endpoint.Host, PostgresRf3Certificate.RootPath(fixture), token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, PostgresRf3Protocol.OneColumn) != PostgresRf3Protocol.FirstColumn)
        {
            return;
        }
        var failures = new List<Exception>();
        if (tls is not null)
        {
            try
            {
                var terminal = tls.DisposeAsync().AsTask();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => terminal, failures);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        if (network is not null)
        {
            try
            {
                var terminal = network.DisposeAsync().AsTask();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => terminal, failures);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        try
        {
            if (Interlocked.Exchange(ref socketDisposed, PostgresRf3Protocol.OneColumn) == PostgresRf3Protocol.FirstColumn)
            {
                socket.Dispose();
            }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
