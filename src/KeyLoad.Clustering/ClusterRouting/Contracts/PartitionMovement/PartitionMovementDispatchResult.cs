using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

/// <summary>Original bounded native reply and its authenticated actual receiver voter.</summary>
internal sealed record PartitionMovementDispatchResult(string VoterId, GrainOperationReply Reply,
    PartitionMoveCaptureWitness? CaptureWitness = null,
    PartitionMoveAuthenticatedOutcomeWitness? OutcomeWitness = null);
