using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record TargetInboxCapacityAuthorityContinuation(Result<object> Healthy, CommitInboxRequest Duplicate,
    Result<object> DuplicateResult, CommitInboxRequest Quota, Result<object> QuotaResult, long HealthyPosition, string[] Image, long Position);
