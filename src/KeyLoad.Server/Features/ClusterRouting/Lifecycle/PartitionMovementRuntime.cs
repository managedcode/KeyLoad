using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Closes transport before draining original operations, source pages and configured peer state.</summary>
internal sealed partial class PartitionMovementRuntime : IPartitionMovementDispatcher, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Dictionary<Guid, TaskCompletionSource> active = [];
    private readonly NativeRequestWorkOwner work;
    private PartitionMovementPeerAdmission? admission;
    private PartitionMovementClient? client;
    private PartitionMovementSourceOwner? source;
    private Task? shutdown;
    private bool closed;
    private bool joined;
    internal bool IsJoined { get { lock (gate) { return joined; } } }
    internal INativePartitionMovementCapture Source => source ?? throw new InvalidOperationException();
    internal PartitionMovementEndpoint? Endpoint { get; private set; }

    private PartitionMovementRuntime(NativeRequestWorkOwner work) => this.work = work;

    internal static async Task<PartitionMovementRuntime?> CreateAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, NativeRequestWorkOwner work, TimeProvider clock)
    {
        if (!options.PartitionMovement.Value.Enabled)
        { return null; }
        if (!options.Node.Value.MembershipAuthority.RegisterPhysicalOwners
            || !options.Node.Value.MembershipAuthority.RemoteDocumentReads)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable); }
        var runtime = new PartitionMovementRuntime(work);
        try
        {
            var receiver = new PartitionMovementReceiver(node, partition, options.Node, clock,
                () => runtime.source ?? throw new InvalidOperationException());
            runtime.source = new(partition.Database, partition.CacheMemory, options.Core.DatabaseLimits,
                work, clock, receiver.SettleCaptureAsync);
            runtime.admission = new(options.Node, partition.Database, options.ReplicaConfiguration,
                options.GrainRouting, options.Membership, options.PartitionMovement, clock);
            runtime.client = new(options.Node, node, partition, options.Membership, clock);
            runtime.Endpoint = new(runtime, runtime.admission, receiver, options.Node,
                options.GrainRouting, options.Core.DatabaseLimits, clock);
            return runtime;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken caller)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (closed)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
            active.Add(id, completion);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
            using var frame = new NativeCapabilityWorkLifetime(linked.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                frame.Admit(work, id, NativeRequestWorkKind.CommandCapability);
                await operation(frame.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        lock (gate)
        {
            completion.TrySetResult();
            active.Remove(id);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
