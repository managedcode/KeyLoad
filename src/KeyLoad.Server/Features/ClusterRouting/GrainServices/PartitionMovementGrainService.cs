using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>One native directly addressed movement endpoint per silo; storage remains node-local.</summary>
/// <param name="id">Native per-silo service identity.</param>
/// <param name="silo">Actual hosting silo.</param>
/// <param name="loggerFactory">Borrowed native diagnostic owner.</param>
internal sealed partial class PartitionMovementGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory)
    : GrainService(id, silo, loggerFactory), IPartitionMovementGrainService
{
    private const int FailureEventId = 3;
    private const int NoShutdownFailures = 0;
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(LogLevel.Error,
        new EventId(FailureEventId), PartitionMovementProtocol.Unavailable);
    private readonly ILogger diagnostics = loggerFactory.CreateLogger<PartitionMovementGrainService>();
    private ReplicaSiloDiscoveryState? discoveryState;
    private ReplicaEnvelopeAuthenticator? authenticator;
    private ReplicaSiloDiscoveryState discovery => discoveryState
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    private ReplicaEnvelopeAuthenticator authentication => authenticator
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    private PartitionMovementNativeReceiver? receiver;
    private PhysicalOwnerProbeNativeRuntime? physicalProbe;

    /// <inheritdoc />
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        var token = StoppedCancellationTokenSource.Token;
        await serviceProvider.GetRequiredService<Func<IPartitionStoragePublication>>()().WaitForOpenAsync(token).ConfigureAwait(true);
        await serviceProvider.GetRequiredService<OrleansNode>().InitializeLocalRuntimeAsync(token).ConfigureAwait(true);
        discoveryState = serviceProvider.GetRequiredService<ReplicaSiloDiscoveryState>();
        authenticator = serviceProvider.GetRequiredService<ReplicaEnvelopeAuthenticator>();
        receiver = serviceProvider.GetService<PartitionMovementNativeReceiver>();
        physicalProbe = serviceProvider.GetService<PhysicalOwnerProbeNativeRuntime>();
        capacity = serviceProvider.GetService<Func<OwnerCapacityNativeLifetime>>()?.Invoke();
        resourceInventory = serviceProvider.GetRequiredService<Func<global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeRuntime>>()();
    }

    /// <inheritdoc />
    public async Task<PartitionMovementGrainServiceReply> ExchangeAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken)
    {
        if (receiver is not { } active)
        {
            return new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty,
                ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable, request?.Nonce ?? Guid.Empty, string.Empty);
        }
        try
        {
            return await active.ExchangeAsync(request, error => LogFailure(diagnostics, error), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            LogFailure(diagnostics, error);
            return new(ReadOnlyMemory<byte>.Empty, string.Empty, ErrorCode.OwnershipLost,
                PartitionMovementProtocol.Unavailable, request?.Nonce ?? Guid.Empty, string.Empty);
        }
    }

    /// <inheritdoc />
    public Task<PartitionMovementGrainServiceReply> ProbeOwnerAsync(PartitionMovementGrainServiceRequest request,
        CancellationToken cancellationToken)
        => physicalProbe is { } active ? active.ProbeOwnerAsync(request, cancellationToken)
            : Task.FromResult(new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty,
                ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable,
                request?.Nonce ?? Guid.Empty, string.Empty));

    /// <inheritdoc />
    public Task<PartitionMovementGrainServiceDiscovery> DiscoverAsync(Guid nonce, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException(PartitionMovementProtocol.Unavailable, nameof(nonce));
        }
        var actual = discovery.Read();
        if (NativeSerialization.Measure(actual) > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        {
            throw new InvalidOperationException(PartitionMovementProtocol.Unavailable);
        }
        var bytes = NativeSerialization.Serialize(actual);
        return Task.FromResult(new PartitionMovementGrainServiceDiscovery(bytes, authentication.SignDiscovery(bytes, nonce)));
    }

    /// <inheritdoc />
    public override async Task Stop()
    {
        var failures = new List<Exception>();
        try
        {
            if (resourceInventory is { } inventory)
            {
                await inventory.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            if (capacity is { } owned)
            {
                await owned.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            if (physicalProbe is { } active)
            {
                await active.StopAsync().ConfigureAwait(true);
            }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            await base.Stop().ConfigureAwait(true);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        if (failures.Count != NoShutdownFailures)
        {
            throw new AggregateException(failures);
        }
    }
}
