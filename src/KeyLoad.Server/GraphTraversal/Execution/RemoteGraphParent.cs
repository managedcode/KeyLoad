using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphParent(OrleansNode node, PartitionHost partition, TimeProvider clock)
    : IRemoteGraphParent
{
    public async Task<RemoteGraphEdgeResultV1> ExecuteAsync(GrainRequestEnvelope trusted, OperationKind kind,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        work.RequireResourceLifetimeBinding(binding);
        var principal = RemoteGraphParentRouting.Principal(partition, clock, trusted, work);
        var effects = new RemoteGraphEffectBoundary();
        try
        {
            var result = await RemoteGraphParentMutation.ExecuteAsync(node, partition, principal,
                trusted, kind, payload, effects, work, binding, token).ConfigureAwait(false);
            work.Check();
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception failure) when (effects.IsStarted && NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphNativeProtocol.Unavailable, failure);
        }
    }

    public Task<RemoteIncomingGraphEdgesPageV1> ReadIncomingAsync(GrainRequestEnvelope trusted,
        ReadRemoteIncomingGraphEdgesRequestV1 request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        work.RequireResourceLifetimeBinding(binding);
        var principal = RemoteGraphParentRouting.Principal(partition, clock, trusted, work);
        return RemoteGraphParentIncoming.ExecuteAsync(node, partition, principal, trusted,
            request, work, token);
    }
}
