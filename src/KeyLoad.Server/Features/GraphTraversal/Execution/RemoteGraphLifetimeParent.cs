using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphLifetimeParent(IRemoteGraphParent original, Func<ReplicaDiscoveryLifetime.Operation> borrowScope)
    : IRemoteGraphParent
{
    public async Task<RemoteGraphEdgeResultV1> ExecuteAsync(GrainRequestEnvelope trusted, OperationKind kind,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget work, CancellationToken caller,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(work);
        work.RequireResourceLifetimeBinding(binding);
        using var operation = borrowScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken);
        using var stage = work.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        var result = await original.ExecuteAsync(trusted, kind, payload, work, linked.Token, binding).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            work.Check();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphNativeProtocol.Unavailable, failure);
        }
        return result;
    }

    public async Task<RemoteIncomingGraphEdgesPageV1> ReadIncomingAsync(GrainRequestEnvelope trusted,
        ReadRemoteIncomingGraphEdgesRequestV1 request, ReadExecutionBudget work, CancellationToken caller,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        work.RequireResourceLifetimeBinding(binding);
        using var operation = borrowScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken);
        using var stage = work.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        var result = await original.ReadIncomingAsync(trusted, request, work, linked.Token, binding).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        return result;
    }
}
