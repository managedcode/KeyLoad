using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphRuntime : IAsyncDisposable
{
    private const string AttachedReceiver = "The original remote graph receiver is already attached.";
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource stopping = new();
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly Lock closing = new();
    private Task? disposal;
    private int joined;
    private const int Joined = 1;
    private IRemoteGraphParent? parent;
    private RemoteGraphNativeRuntime? receiver;

    private RemoteGraphRuntime(OrleansNode node, PartitionHost partition, TimeProvider clock,
        Microsoft.Extensions.Options.IOptions<GrainRoutingOptions> options)
    {
        this.node = node;
        this.partition = partition;
        this.clock = clock;
        _ = options.Value;
        lifetime = new(stopping, () => Volatile.Write(ref joined, Joined));
    }

    internal IRemoteGraphParent Parent => Volatile.Read(ref parent)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);
    internal bool IsJoined => Volatile.Read(ref joined) == Joined;

    internal static async Task<RemoteGraphRuntime?> CreateAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        if (options.Node.Value.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled)
        {
            return null;
        }
        _ = options.GrainRouting.Value;
        var original = new RemoteGraphRuntime(node, partition, clock, options.GrainRouting);
        try
        {
            original.parent = new RemoteGraphLifetimeParent(new RemoteGraphParent(node, partition, clock), original.EnterScope);
            return original;
        }
        catch (Exception primary) when (NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            try
            {
                await original.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catch (Exception primary) when (!NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var failures = new List<Exception> { primary };
            try
            {
                await original.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void AttachNativeReceiver(RemoteGraphNativeRuntime original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Interlocked.CompareExchange(ref receiver, original, null) is not null)
        {
            throw new InvalidOperationException(AttachedReceiver);
        }
        original.Attach(this);
    }

    internal async Task<ReadOnlyMemory<byte>> ExecuteNativeAsync(RemoteGraphNativeRequestV1 request,
        RemoteGraphEffectBoundary boundary, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(budget);
        budget.RequireResourceLifetimeBinding(binding);
        using var operation = EnterScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken);
        using var stage = budget.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        budget.Check();
        var result = await RemoteGraphNativeDispatch.ExecuteAsync(node, partition, clock, request, boundary,
            budget, binding, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        budget.Check();
        return result;
    }

    internal ReplicaDiscoveryLifetime.Operation EnterScope()
        => lifetime.TryEnter() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable);

    public ValueTask DisposeAsync()
    {
        lock (closing)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(lifetime.StopAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(stopping.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
