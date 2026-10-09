using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>Closed configured movement transport actions; none imply public data authorization.</summary>
internal enum PartitionMovementPeerAction
{
    Apply = 1,
    Capture = 2,
    Page = 3,
    Release = 4
}

/// <summary>Original bounded native reply and its authenticated actual receiver voter.</summary>
internal sealed record PartitionMovementDispatchResult(string VoterId, GrainOperationReply Reply,
    PartitionMoveCaptureWitness? CaptureWitness = null,
    PartitionMoveAuthenticatedOutcomeWitness? OutcomeWitness = null);
