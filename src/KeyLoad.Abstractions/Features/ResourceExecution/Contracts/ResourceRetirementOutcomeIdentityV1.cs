namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementOutcomeIdentityAlias)]
internal sealed record ResourceRetirementOutcomeIdentityV1(
    [property: Orleans.Id(0)] string PrincipalId,
    [property: Orleans.Id(1)] long PolicyEpoch,
    [property: Orleans.Id(2)] string Fingerprint);
