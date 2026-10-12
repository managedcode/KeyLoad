using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphParentIncoming
{
    internal static async Task<RemoteIncomingGraphEdgesPageV1> ExecuteAsync(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, GrainRequestEnvelope trusted, ReadRemoteIncomingGraphEdgesRequestV1 request,
        ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        if (request.Version != RemoteGraphProtocol.Version)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof); }
        var route = RemoteGraphParentRouting.Incoming(node, partition, principal, request.Target, request.Graph, work);
        var query = new RemoteGraphReceiverCandidatesRequestV1(request.Target, request.Graph, request.Limit);
        var candidates = await RemoteGraphParentCall.RemoteAsync<RemoteGraphReceiverCandidatesV1>(node, partition,
            principal, route.Target, RemoteGraphNativeAction.ReceiverCandidates, query,
            trusted.ExpiresAt, work, token).ConfigureAwait(false);
        if (candidates is null || candidates.States.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof); }
        if (candidates.States.Length > partition.Database.Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphNativeProtocol.InvalidProof); }
        return await RemoteGraphParentCall.RemoteAsync<RemoteIncomingGraphEdgesPageV1>(node, partition,
            principal, route.Target, RemoteGraphNativeAction.IncomingFinalize,
            new RemoteGraphIncomingFinalizeRequestV1(candidates, ImmutableArray<RemoteGraphIncomingSourceProofV1>.Empty),
            trusted.ExpiresAt, work, token).ConfigureAwait(false);
    }
}
