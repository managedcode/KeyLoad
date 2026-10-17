namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparedReceiptAuthorityAlias)]
internal sealed record ResourceRetirementPreparedReceiptAuthorityV1(
    [property: Orleans.Id(0)] string PrincipalId,
    [property: Orleans.Id(1)] long PolicyEpoch,
    [property: Orleans.Id(2)] string OperationFingerprint,
    [property: Orleans.Id(3)] DateTimeOffset EvaluatedAt);
