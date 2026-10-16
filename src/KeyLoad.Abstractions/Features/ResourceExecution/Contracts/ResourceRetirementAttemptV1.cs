namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementAttemptAlias)]
internal sealed record ResourceRetirementAttemptV1(
    [property: Orleans.Id(0)] Guid AttemptId,
    [property: Orleans.Id(1)] long Ordinal,
    [property: Orleans.Id(2)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(3)] ResourceRetirementPhaseReceiptV1 PrepareReceipt,
    [property: Orleans.Id(4)] ResourceRetirementAttemptStage Stage,
    [property: Orleans.Id(5)] string PrincipalId,
    [property: Orleans.Id(6)] long PolicyEpoch,
    [property: Orleans.Id(7)] Guid OriginalCommandId,
    [property: Orleans.Id(8)] string? OriginalFingerprint,
    [property: Orleans.Id(9)] ResourceRetirementPhaseReceiptV1? TerminalReceipt,
    [property: Orleans.Id(10)] ErrorCode? Error,
    [property: Orleans.Id(11)] string? SafeDetail);
