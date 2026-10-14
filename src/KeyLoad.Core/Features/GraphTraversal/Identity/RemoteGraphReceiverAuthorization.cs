using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private AtomicPartitionPlacementResolution? AuthorizeRemoteGraphReceive(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var command = Payload<RemoteGraphReceiverMutationV1>(operation);
        if (command.Version != RemoteGraphProtocol.Version || command.CommandId != operation.Id
            || string.IsNullOrEmpty(command.VerifiedToken))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        var placement = RequireRemoteGraphReceiver(view, principal, command.SourceProof);
        var claims = Verify<RemoteGraphReceiverClaimsV1>(command.VerifiedToken);
        if (claims.Purpose != RemoteGraphProtocol.ReceiverPurpose || claims.TargetPrincipalId != principal.Id
            || claims.TargetPolicyEpoch != principal.PolicyEpoch
            || !NativeSerialization.Serialize(claims.SourceProof).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(command.SourceProof)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        return placement;
    }

    private AtomicPartitionPlacementResolution RequireRemoteGraphReceiver(IKeyValueView view,
        PrincipalRecord principal, RemoteGraphSourceProofV1 proof, ReadExecutionBudget? work = null)
    {
        RequireRemoteGraphForeignIntent(view, proof);
        if (proof.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        var content = proof.Intent.Content;
        Authorization.Require(principal, content.Destination.Partition, content.Graph, Capability.GraphWrite);
        _ = Resource(view, content.Destination.Partition, content.Graph, ResourceKind.Graph);
        _ = Resource(view, content.Destination.Partition, content.Destination.Collection, ResourceKind.Collection);
        if (!content.Deleted)
        {
            _ = VisibleVertex(view, principal, content.Destination, work);
        }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, proof.Intent.DestinationOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
        return ReadPlacementWitness(view, content.Destination.Partition);
    }
}
