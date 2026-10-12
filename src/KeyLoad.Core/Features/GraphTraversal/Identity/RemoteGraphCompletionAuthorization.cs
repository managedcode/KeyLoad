using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private AtomicPartitionPlacementResolution? AuthorizeRemoteGraphComplete(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var command = Payload<RemoteGraphCompletionMutationV1>(operation);
        if (command.Version != RemoteGraphProtocol.Version || command.CommandId != operation.Id
            || string.IsNullOrEmpty(command.VerifiedToken))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
        var placement = RequireRemoteGraphCompletion(view, principal, command.Receipt);
        var claims = Verify<RemoteGraphCompletionClaimsV1>(command.VerifiedToken);
        if (claims.Purpose != RemoteGraphProtocol.CompletionPurpose || claims.SourcePrincipalId != principal.Id
            || claims.SourcePolicyEpoch != principal.PolicyEpoch
            || !NativeSerialization.Serialize(claims.Receipt).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(command.Receipt)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
        return placement;
    }

    private AtomicPartitionPlacementResolution RequireRemoteGraphCompletion(IKeyValueView view,
        PrincipalRecord principal, RemoteGraphReceiptV1 receipt)
    {
        RequireRemoteGraphReceiptShape(receipt, ErrorCode.Unauthenticated);
        var intent = receipt.Intent;
        var content = intent.Content;
        Authorization.Require(principal, content.SourcePartition, content.Graph, Capability.GraphWrite);
        _ = Resource(view, content.SourcePartition, content.Graph, ResourceKind.Graph);
        _ = VisibleVertex(view, principal, content.Edge.From);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        RequireRemoteGraphStoredIntent(view, content.SourcePartition, content.Graph, content.EdgeId,
            catalog.DefaultShard, intent);
        return ReadPlacementWitness(view, content.SourcePartition);
    }
}
