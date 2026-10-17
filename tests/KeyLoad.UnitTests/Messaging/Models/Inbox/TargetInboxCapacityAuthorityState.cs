namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record TargetInboxCapacityAuthorityState(CommitInboxRequest Original, CommitInboxRequest Second,
    CommitInboxRequest Healthy, byte[] Capacity, byte[] OriginalRecord, byte[] SecondRecord,
    OperationResult OriginalResult, OperationResult SecondResult, DeliveryCommand SourceAck, OperationResult SourceAckResult);
