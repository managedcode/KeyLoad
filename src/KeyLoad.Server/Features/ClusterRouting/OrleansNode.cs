using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Owns one native silo lifetime and routes public operations to uniquely keyed request actors.</summary>
/// <param name="partition">Borrowed physical database and replica owner.</param>
/// <param name="options">Validated fixed-voter silo settings.</param>
/// <param name="administration">Borrowed node administration guarded by read actors.</param>
/// <param name="loggerFactory">Shared process diagnostics, owned by the outer application.</param>
internal sealed class OrleansNode(PartitionHost partition, NodeOptions options, INodeAdministration administration,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly object lifecycle = new();
    private IHost? host;
    private IGrainFactory? grains;
    private Task? shutdown;
    private Task? startup;

    /// <summary>Factory published only after native silo startup completes.</summary>
    public IGrainFactory? Grains => Volatile.Read(ref grains);

    /// <summary>Early runtime discovery remains available during membership bootstrap.</summary>
    public ReplicaSiloDiscoveryState? Discovery => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryState>();

    /// <summary>Authenticator for exact discovery response bytes.</summary>
    public ReplicaEnvelopeAuthenticator? Authentication => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaEnvelopeAuthenticator>();

    /// <summary>Starts the native transport before its quorum-backed membership provider initializes.</summary>
    /// <param name="cancellationToken">Application startup cancellation, shared with membership initialization.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (startup is not null || shutdown is not null)
            { throw new InvalidOperationException(OrleansNodeProtocol.SiloAlreadyStarted); }
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            startup = StartCoreAsync(registered.Task, cancellationToken);
            registered.SetResult();
            return startup;
        }
    }

    private async Task StartCoreAsync(Task registered, CancellationToken cancellationToken)
    {
        await registered.ConfigureAwait(false);
        var address = await ResolveAddressAsync(cancellationToken).ConfigureAwait(false);
        var built = OrleansSiloConfiguration.Build(partition, options, administration, loggerFactory, address, cancellationToken);
        Volatile.Write(ref host, built);
        await built.StartAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref grains, built.Services.GetRequiredService<IGrainFactory>());
    }

    private async Task<IPAddress> ResolveAddressAsync(CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(options.SiloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(options.SiloAddress, cancellationToken).ConfigureAwait(false);
        var allowLoopback = options.Peers.All(peer => new Uri(peer).IsLoopback);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any) && (allowLoopback || !IPAddress.IsLoopback(address)))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
    }

    /// <summary>Executes one signed operation through the corresponding GUID request actor.</summary>
    /// <param name="requestId">Fresh public request ID, distinct from the stable command ID.</param>
    /// <param name="signedRequest">Bounded trusted envelope issued by the shared request codec.</param>
    /// <param name="command">Server-derived command intent used only to classify an uncertain RPC outcome.</param>
    /// <param name="cancellationToken">Public request cancellation propagated through Orleans.</param>
    /// <returns>Exact bounded reply, throwing only the typed public safe error locally.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(Guid requestId, string signedRequest, bool command,
        CancellationToken cancellationToken)
    {
        var factory = Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        GrainOperationReply reply;
        try
        {
            reply = await factory.GetGrain<IRequestGrain>(requestId).ExecuteAsync(signedRequest, cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (OrleansRpcFailure.IsNative(failure))
        {
            throw OrleansRpcFailure.Translate(failure, command, requestId, loggerFactory.CreateLogger<OrleansNode>(),
                cancellationToken);
        }
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? OrleansNodeProtocol.ReplyRejected); }
        return reply;
    }

    /// <summary>Stops membership before its replica lifecycle drains and before physical stores are released.</summary>
    /// <param name="cancellationToken">Cancellation bounds this caller's wait, without interrupting owned cleanup.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (shutdown is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = StopCoreAsync(registered.Task, startup);
                registered.SetResult();
            }
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync(Task registered, Task? starting)
    {
        await registered.ConfigureAwait(false);
        if (starting is not null)
        { await starting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        Volatile.Write(ref grains, null);
        var stopping = Volatile.Read(ref host);
        if (stopping is null)
        { return; }
        using var deadline = new CancellationTokenSource(OrleansNodeProtocol.ShutdownTimeout);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => stopping.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        // A failed native Start has no lifecycle rollback. Drain our borrowed endpoint while its transport still exists.
        ServerFailureObserver.Observe(() => stopping.Services.GetService<ReplicaSiloDiscoveryState>()?.StopDiscovery(), failures);
        await ServerFailureObserver.ObserveAsync(() => partition.Consensus.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        Volatile.Write(ref host, null);
        await ServerFailureObserver.ObserveAsync(() => DisposeHostAsync(stopping), failures).ConfigureAwait(false);
        if (failures.Count == 1)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1)
        { throw new AggregateException(failures); }
    }

    private static async Task DisposeHostAsync(IHost stopping)
    {
        if (stopping is IAsyncDisposable asynchronous)
        { await asynchronous.DisposeAsync().ConfigureAwait(false); }
        else
        { stopping.Dispose(); }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
}
