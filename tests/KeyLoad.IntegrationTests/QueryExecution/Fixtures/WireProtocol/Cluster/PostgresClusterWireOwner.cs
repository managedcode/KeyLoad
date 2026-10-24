using System.Net.Security;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed class PostgresClusterWireOwner : IAsyncDisposable
{
    private readonly TcpClient socket = new();
    private NetworkStream? network;
    private SslStream? tls;
    private int disposed;
    private int receiverAborted;
    private int socketDisposed;
    internal bool ReceiverAborted => Volatile.Read(ref receiverAborted) != PostgresClusterProtocol.FirstColumn;

    internal void AbortReceiver()
    {
        if (Interlocked.Exchange(ref receiverAborted, PostgresClusterProtocol.OneColumn) != PostgresClusterProtocol.FirstColumn)
        {
            return;
        }
        socket.LingerState = new LingerOption(true, PostgresClusterProtocol.FirstColumn);
        if (Interlocked.Exchange(ref socketDisposed, PostgresClusterProtocol.OneColumn) == PostgresClusterProtocol.FirstColumn)
        {
            socket.Dispose();
        }
    }
    internal SslStream Stream => tls ?? throw new InvalidOperationException(PostgresClusterProtocol.InvalidResult);

    private PostgresClusterWireOwner()
    {
    }

    internal static async Task<PostgresClusterWireOwner> OpenAsync(ClusterFixture fixture, CancellationToken token)
    {
        var owner = new PostgresClusterWireOwner();
        try
        {
            var endpoint = fixture.App.GetEndpoint(McpCallerProtocol.Node3, PostgresClusterProtocol.Endpoint);
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
        socket.ReceiveBufferSize = PostgresClusterBackpressureProtocol.ReceiveBytes;
        await global::TUnit.Assertions.Assert.That(socket.ReceiveBufferSize).IsLessThanOrEqualTo(PostgresClusterBackpressureProtocol.MaximumEffectiveReceiveBytes);
        await socket.ConnectAsync(endpoint.Host, endpoint.Port, token).ConfigureAwait(false);
        network = socket.GetStream();
        await network.WriteAsync(PostgresClusterWireFrames.SslRequest(), token).ConfigureAwait(false);
        var accepted = new byte[PostgresClusterProtocol.OneColumn];
        await network.ReadExactlyAsync(accepted, token).ConfigureAwait(false);
        await global::TUnit.Assertions.Assert.That(accepted[PostgresClusterProtocol.FirstColumn]).IsEqualTo(PostgresClusterWireFrames.SslAccepted);
        tls = new SslStream(network, leaveInnerStreamOpen: true);
        await PostgresClusterWireTrust.AuthenticateAsync(tls, endpoint.Host, PostgresClusterCertificate.RootPath(fixture), token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, PostgresClusterProtocol.OneColumn) != PostgresClusterProtocol.FirstColumn)
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
            catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
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
            catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        try
        {
            if (Interlocked.Exchange(ref socketDisposed, PostgresClusterProtocol.OneColumn) == PostgresClusterProtocol.FirstColumn)
            {
                socket.Dispose();
            }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
