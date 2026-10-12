using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static Dictionary<(PartitionRef Source, string Edge), RemoteGraphIncomingSourceProofV1>
        IndexRemoteGraphIncomingProofs(RemoteGraphIncomingFinalizeRequestV1 request, ReadExecutionBudget work)
    {
        var result = new Dictionary<(PartitionRef, string), RemoteGraphIncomingSourceProofV1>();
        foreach (var proof in request.Proofs)
        {
            work.Check();
            if (proof?.Intent?.Content is null || proof.Version != RemoteGraphProtocol.Version
                || proof.SourceCut?.Placement is null || proof.SourceCut.Owner is null
                || !result.TryAdd((proof.Intent.Content.SourcePartition, proof.Intent.Content.EdgeId), proof))
            { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        }
        return result;
    }

    private void RequireRemoteGraphIncomingProof(RemoteGraphReceiverStateV1 state,
        RemoteGraphIncomingSourceProofV1 proof, ReadExecutionBudget work)
    {
        if (!RemoteGraphIncomingBytesEqual(state.Intent, proof.Intent, work)
            || !PhysicalOwnerEntryValidation.SameOwner(state.Intent.SourceOwner, proof.SourceCut.Owner)
            || proof.SourceCut.Placement.Partition != state.Content.SourcePartition
            || proof.SourceCut.Placement.PhysicalShardId != state.Intent.SourceOwner.PhysicalShardId
            || proof.SourceCut.Placement.Incarnation != state.Intent.SourceOwner.Incarnation
            || !proof.SourceCut.Placement.VoterIds.SequenceEqual(state.Intent.SourceOwner.VoterIds, StringComparer.Ordinal)
            || proof.SourceCut.Incarnation != proof.SourceCut.Placement.Incarnation
            || proof.SourceCut.NodeId == Guid.Empty || proof.SourceCut.ReadGeneration < RemoteGraphProtocol.MinimumReadGeneration
            || proof.SourceCut.SchemaVersion < RemoteGraphProtocol.MinimumSchemaVersion || proof.SourceCut.PolicyEpoch < RemoteGraphProtocol.MinimumPolicyEpoch
            || proof.SourceCut.StorePosition < state.Intent.SourceCommit.Position
            || proof.SourceCut.AppliedPosition < state.Intent.SourceCommit.Position
            || proof.SourceCut.Placement.PlacementEpoch != state.Intent.SourceCommit.OwnershipEpoch)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
        if (proof.Edge is not { } edge)
        { return; }
        GraphCrossPartitionValidation.ValidateEdge(edge, Limits);
        if (edge.Id != state.Content.EdgeId || edge.From.Partition != state.Content.SourcePartition
            || edge.To != state.Content.Destination || edge.Revision != state.Content.Revision)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
    }
}
