namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparedOperationAlias)]
internal sealed record ResourceRetirementPreparedOperationV1(
    [property: Orleans.Id(0)] ResourceRetirementAttemptContextV1 Context,
    [property: Orleans.Id(1)] ResourceRetirementAttemptV1 Attempt,
    [property: Orleans.Id(2)] ReplicatedOperation? Original,
    [property: Orleans.Id(3)] string? Fingerprint,
    [property: Orleans.Id(4)] ResourceRetirementPreparedOperationStage Stage,
    [property: Orleans.Id(5)] ResourceRetirementPhaseReceiptV1 Receipt,
    [property: Orleans.Id(6)] ResourceRetirementPhaseReceiptV1? PersistReceipt,
    [property: Orleans.Id(7)] ErrorCode? Error,
    [property: Orleans.Id(8)] string? SafeDetail)
{
    [Orleans.Id(9)]
    public ResourceRetirementPreparedReceiptAuthorityV1? ReceiptAuthority
    {
        get; init;
    }

    [Orleans.Id(10)]
    public ResourceRetirementPreparedReceiptAuthorityV1? PersistAuthority
    {
        get; init;
    }
}
