namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPrepareAttemptBodyAlias)]
internal sealed record ResourceRetirementPrepareAttemptBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementAttemptContextV1 Context,
    [property: Orleans.Id(1)] Guid AttemptId,
    [property: Orleans.Id(2)] long ExpectedOrdinal,
    [property: Orleans.Id(3)] DateTimeOffset ExpiresAt)
{
    [Orleans.Id(4)]
    public ResourceRetirementPreparedOperationV1? PreviousClosed
    {
        get; init;
    }
}
