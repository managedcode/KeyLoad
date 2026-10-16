using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeDispatch
{
    internal static Task<ReadOnlyMemory<byte>> ExecuteAsync(OrleansNode node, PartitionHost partition,
        TimeProvider clock, RemoteGraphNativeRequestV1 request, RemoteGraphEffectBoundary boundary,
        ReadExecutionBudget work, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(work);
        work.RequireResourceLifetimeBinding(binding);
        work.Check();
        token.ThrowIfCancellationRequested();
        var principal = partition.Database.Store.Read(view =>
            partition.Database.Principal(work.CreateView(view), request.PrincipalId, clock.GetUtcNow()));
        return request.Action switch
        {
            RemoteGraphNativeAction.TargetPreparation => RemoteGraphNativeChild.ReadAsync<RemoteGraphTargetPreparationV1>(
                node, partition, principal, request, GrainReadKind.RemoteGraphTargetPreparation, work, token, binding),
            RemoteGraphNativeAction.SourceIntent => RemoteGraphNativeChild.ReadAsync<RemoteGraphSourceSnapshotV1>(
                node, partition, principal, request, GrainReadKind.RemoteGraphSourceIntent, work, token, binding),
            RemoteGraphNativeAction.ReceiverApply => ReceiveAsync(node, partition, principal, request, boundary, work, token, binding),
            RemoteGraphNativeAction.ReceiverReceipt => RemoteGraphNativeChild.ReadAsync<RemoteGraphReceiverResultV1>(
                node, partition, principal, request, GrainReadKind.RemoteGraphReceiverReceipt, work, token, binding),
            RemoteGraphNativeAction.IncomingSourceProof => RemoteGraphNativeChild.ReadAsync<RemoteGraphIncomingSourceProofV1>(
                node, partition, principal, request, GrainReadKind.RemoteGraphIncoming, work, token, binding),
            RemoteGraphNativeAction.ReceiverCandidates => RemoteGraphNativeChild.ReadAsync<RemoteGraphReceiverCandidatesV1>(
                node, partition, principal, request, GrainReadKind.RemoteGraphReceiverCandidates, work, token, binding),
            RemoteGraphNativeAction.IncomingFinalize => FinalizeAsync(node, partition, principal, request, work, token, binding),
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof)
        };
    }

    private static async Task<ReadOnlyMemory<byte>> FinalizeAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, RemoteGraphNativeRequestV1 request, ReadExecutionBudget work, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        var original = NativeSerialization.Deserialize<RemoteGraphIncomingFinalizeRequestV1>(request.Payload.Span);
        var actual = await RemoteGraphFreshIncomingObservation.RefreshAsync(node, partition, request,
            original, work, token).ConfigureAwait(false);
        work.Check();
        token.ThrowIfCancellationRequested();
        var result = await RemoteGraphParentCall.ReadAsync<RemoteIncomingGraphEdgesPageV1>(node, partition,
            principal, GrainReadKind.RemoteGraphIncomingFinalize, actual, request.ExpiresAt,
            work, token, binding).ConfigureAwait(false);
        return RemoteGraphNativeResultEncoding.Encode(result, partition.Database.Limits.MaxBatchBytes, work);
    }

    private static async Task<ReadOnlyMemory<byte>> ReceiveAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, RemoteGraphNativeRequestV1 request, RemoteGraphEffectBoundary boundary,
        ReadExecutionBudget work, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        work.RequireResourceLifetimeBinding(binding);
        var mutation = NativeSerialization.Deserialize<RemoteGraphReceiverMutationV1>(request.Payload.Span);
        mutation = await RemoteGraphFreshSourceObservation.RefreshAsync(node, partition, request, mutation,
            work, token).ConfigureAwait(false);
        work.Check();
        token.ThrowIfCancellationRequested();
        var operation = partition.Database.CreateVerifiedRemoteGraphReceive(principal.Id, mutation, request.ExpiresAt, work);
        var childId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().RemoteGraph.CreateRemoteGraphPhase(childId, operation, request.ExpiresAt);
        return await RemoteGraphNativeChild.ExecuteAsync<RemoteGraphReceiverResultV1>(node, partition, principal,
            childId, operation.Id, signed, command: true, boundary, work, token).ConfigureAwait(false);
    }
}
