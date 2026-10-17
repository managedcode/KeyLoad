namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record TargetInboxCapacityAuthorityState(CommitInboxRequest Original, CommitInboxRequest Second,
    CommitInboxRequest Healthy, byte[] Capacity, byte[] OriginalRecord, byte[] SecondRecord,
    OperationResult OriginalResult, OperationResult SecondResult, DeliveryCommand SourceAck, OperationResult SourceAckResult);

internal sealed record TargetInboxCapacityAuthorityContinuation(OperationResult Healthy, CommitInboxRequest Duplicate,
    OperationResult DuplicateResult, CommitInboxRequest Quota, OperationResult QuotaResult, long HealthyPosition, string[] Image, long Position);
