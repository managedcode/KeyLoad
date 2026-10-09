using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Closes transport before draining original operations, source pages and configured peer state.</summary>
internal sealed partial class PartitionMovementRuntime : IPartitionMovementDispatcher, IPartitionMovementParent, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Dictionary<Guid, TaskCompletionSource> active = [];
    private readonly PartitionMovementRuntimeExecution execution;
    private PartitionMovementPeerAdmission? admission;
    private PartitionMovementClient? client;
    private PartitionMovementSourceOwner? source;
    private PartitionMovementParentOrchestrator? parent;
    private DatabaseEngine? parentDatabase;
    private TimeProvider? parentClock;
    private Task? shutdown;
    private bool closed;
    private bool joined;
    internal bool IsJoined { get { lock (gate) { return joined; } } }
    internal INativePartitionMovementCapture Source => source ?? throw new InvalidOperationException();
    internal PartitionMovementEndpoint? Endpoint { get; private set; }

    internal PartitionMovementRuntime(NativeRequestWorkOwner work)
        => execution = new(gate, stopping, active, work, () => closed);

    internal static Task<PartitionMovementRuntime?> CreateAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, NativeRequestWorkOwner work, TimeProvider clock)
        => PartitionMovementRuntimeFactory.CreateAsync(node, partition, options, work, clock);

    internal void Initialize(OrleansNode node, PartitionHost partition, ServerRuntimeOptions options,
        NativeRequestWorkOwner work, TimeProvider clock)
    {
        var receiver = new PartitionMovementReceiver(node, partition, options.Node, clock,
            () => source ?? throw new InvalidOperationException());
        source = new(partition.Database, partition.CacheMemory, options.Core.DatabaseLimits,
            work, clock, receiver.SettleCaptureAsync);
        admission = new(options.Node, partition.Database, options.ReplicaConfiguration,
            options.GrainRouting, options.Membership, options.PartitionMovement, clock);
        client = new(options.Node, node, partition, options.Membership, clock);
        parentDatabase = partition.Database;
        parentClock = clock;
        parent = new(receiver, client, clock, options.GrainRouting,
            options.Core.DatabaseLimits, options.Node.Value.ClusterId);
        Endpoint = new(this, admission, receiver, options.Node,
            options.GrainRouting, options.Core.DatabaseLimits, clock);
    }

    internal Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken caller)
        => execution.RunAsync(operation, caller);
}
