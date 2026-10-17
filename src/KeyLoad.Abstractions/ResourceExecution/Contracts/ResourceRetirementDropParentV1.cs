namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementDropParentAlias)]
internal sealed record ResourceRetirementDropParentV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] ResourceRetirementDropParentLookupV1 Lookup,
    [property: Orleans.Id(2)] string PrincipalId,
    [property: Orleans.Id(3)] long PolicyEpoch,
    [property: Orleans.Id(4)] string PublicFingerprint,
    [property: Orleans.Id(5)] ResourceDefinition Definition,
    [property: Orleans.Id(6)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(7)] ResourceRetirementRequestV1 RetirementRequest,
    [property: Orleans.Id(8)] ReplicatedOperation OriginalIntent,
    [property: Orleans.Id(9)] ResourceRetirementPhaseReceiptV1 PersistReceipt,
    [property: Orleans.Id(10)] string? ResultJson,
    [property: Orleans.Id(11)] ReplicatedOperation? OriginalCompletion,
    [property: Orleans.Id(12)] ResourceRetirementPhaseReceiptV1? CompleteReceipt,
    [property: Orleans.Id(13)] long? CompletionPolicyEpoch);
