using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>One native directly addressed movement endpoint per silo; storage remains node-local.</summary>
/// <param name="id">Native per-silo service identity.</param>
/// <param name="silo">Actual hosting silo.</param>
/// <param name="loggerFactory">Borrowed native diagnostic owner.</param>
/// <param name="discovery">Actual local native generation and reader capability.</param>
/// <param name="authentication">Existing local discovery signature owner.</param>
internal sealed class PartitionMovementGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    ReplicaSiloDiscoveryState discovery, ReplicaEnvelopeAuthenticator authentication)
    : GrainService(id, silo, loggerFactory), IPartitionMovementGrainService
{
    private const int FailureEventId = 3;
    private const int NoShutdownFailures = 0;
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(LogLevel.Error,
        new EventId(FailureEventId), PartitionMovementProtocol.Unavailable);
    private readonly ILogger diagnostics = loggerFactory.CreateLogger<PartitionMovementGrainService>();
    private PartitionMovementNativeReceiver? receiver;
    private PhysicalOwnerProbeNativeRuntime? physicalProbe;

    /// <inheritdoc />
    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        receiver = serviceProvider.GetService<PartitionMovementNativeReceiver>();
        physicalProbe = serviceProvider.GetService<PhysicalOwnerProbeNativeRuntime>();
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
        try { return await active.ExchangeAsync(request, error => LogFailure(diagnostics, error), cancellationToken).ConfigureAwait(true); }
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
        { throw new ArgumentException(PartitionMovementProtocol.Unavailable, nameof(nonce)); }
        var actual = discovery.Read();
        if (NativeSerialization.Measure(actual) > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        { throw new InvalidOperationException(PartitionMovementProtocol.Unavailable); }
        var bytes = NativeSerialization.Serialize(actual);
        return Task.FromResult(new PartitionMovementGrainServiceDiscovery(bytes, authentication.SignDiscovery(bytes, nonce)));
    }

    /// <inheritdoc />
    public override async Task Stop()
    {
        var failures = new List<Exception>();
        try
        {
            if (physicalProbe is { } active)
            { await active.StopAsync().ConfigureAwait(true); }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try { await base.Stop().ConfigureAwait(true); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        if (failures.Count != NoShutdownFailures)
        { throw new AggregateException(failures); }
    }
}
