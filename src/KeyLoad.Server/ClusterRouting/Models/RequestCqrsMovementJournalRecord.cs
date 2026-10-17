using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsMovementJournalRecord(
    int Version, string Kind, string SessionId, Guid ArmId, Guid RequestId, Guid CommandId,
    Guid ConnectionId, string PrincipalId, string Voter, string SiloAddress,
    PartitionRef Partition, Guid MoveId, PartitionMovePeerStage? Stage,
    PartitionMoveJournalReceipt? Journal, ErrorCode? Error);
