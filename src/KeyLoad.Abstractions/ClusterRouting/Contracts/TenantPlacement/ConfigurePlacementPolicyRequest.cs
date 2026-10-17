namespace KeyLoad;

/// <summary>Defines the bounded ConfigurePlacementPolicyRequest contract.</summary>
/// <param name="ExpectedRevision">The original ExpectedRevision contract value.</param>
/// <param name="Policy">The original Policy contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPolicyV1Aliases.ConfigurePlacementPolicyRequestAlias)]
public sealed record ConfigurePlacementPolicyRequest(
    [property: global::Orleans.Id(0)] long ExpectedRevision,
    [property: global::Orleans.Id(1)] TenantPlacementPolicyV1 Policy,
    [property: global::Orleans.Id(2)] PartitionRef ControlPartition);
