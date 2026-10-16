namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Only owned bytes and scalar values survive the pre-submit caller boundary; no original operation reference is retained.</summary>
internal sealed record ResourceRetirementOriginal8Expectation(
    byte[] OperationBytes,
    byte[] NativePayload,
    string PayloadJson,
    Guid CommandId,
    string PrincipalId,
    DateTimeOffset EvaluatedAt,
    string Fingerprint,
    byte[] PreparedBytes);
