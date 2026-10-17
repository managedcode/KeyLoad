namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record TargetInboxCapacityAuthorityContinuation(OperationResult Healthy, CommitInboxRequest Duplicate,
    OperationResult DuplicateResult, CommitInboxRequest Quota, OperationResult QuotaResult, long HealthyPosition, string[] Image, long Position);
