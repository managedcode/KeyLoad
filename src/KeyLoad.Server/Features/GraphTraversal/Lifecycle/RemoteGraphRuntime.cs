using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphRuntime : IAsyncDisposable
{
    private const string AttachedReceiver = "The original remote graph receiver is already attached.";
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly TimeProvider clock;
    private readonly NativeRequestWorkOwner work;
    private IRemoteGraphParent? parent;
    private RemoteGraphNativeRuntime? receiver;

    private RemoteGraphRuntime(OrleansNode node, PartitionHost partition, TimeProvider clock,
        Microsoft.Extensions.Options.IOptions<GrainRoutingOptions> options)
    {
        this.node = node;
        this.partition = partition;
        this.clock = clock;
        work = new(options);
    }

    internal IRemoteGraphParent Parent => Volatile.Read(ref parent)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
    internal bool IsJoined => work.IsJoined;

    internal static async Task<RemoteGraphRuntime?> CreateAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        if (options.Node.Value.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
        { return null; }
        _ = options.GrainRouting.Value;
        var original = new RemoteGraphRuntime(node, partition, clock, options.GrainRouting);
        try
        {
            original.parent = new RemoteGraphLifetimeParent(new RemoteGraphParent(node, partition, clock), original.work);
            return original;
        }
        catch (Exception primary) when (NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            try
            { await original.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catch (Exception primary) when (!NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            try
            { await original.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void AttachNativeReceiver(RemoteGraphNativeRuntime original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Interlocked.CompareExchange(ref receiver, original, null) is not null)
        { throw new InvalidOperationException(AttachedReceiver); }
        original.Attach(this);
    }

    internal async Task<ReadOnlyMemory<byte>> ExecuteNativeAsync(RemoteGraphNativeRequestV1 request,
        RemoteGraphEffectBoundary boundary, ReadExecutionBudget budget, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(budget);
        using var operation = work.Acquire(request.RequestId, RemoteGraphNativeProtocol.MayHaveEffects(request.Action)
            ? NativeRequestWorkKind.CommandCapability : NativeRequestWorkKind.ReadCapability);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, work.ShutdownToken);
        using var stage = budget.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        budget.Check();
        var result = await RemoteGraphNativeDispatch.ExecuteAsync(node, partition, clock, request, boundary,
            budget, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        budget.Check();
        return result;
    }

    public ValueTask DisposeAsync() => work.DisposeAsync();
}
