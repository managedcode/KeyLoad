namespace KeyLoad;

/// <summary>Defines the bounded PlacementIntentClaimV1 contract.</summary>
/// <param name="VoterId">The original VoterId contract value.</param>
/// <param name="SiloAddress">The original SiloAddress contract value.</param>
/// <param name="CohortFingerprint">The original CohortFingerprint contract value.</param>
/// <param name="Nonce">The original Nonce contract value.</param>
/// <param name="ExpiresAt">The original ExpiresAt contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementIntentClaimV1Aliases.PlacementIntentClaimV1Alias)]
public sealed record PlacementIntentClaimV1(
    [property: global::Orleans.Id(0)] string VoterId,
    [property: global::Orleans.Id(1)] string SiloAddress,
    [property: global::Orleans.Id(2)] string CohortFingerprint,
    [property: global::Orleans.Id(3)] Guid Nonce,
    [property: global::Orleans.Id(4)] DateTimeOffset ExpiresAt);

internal static class PlacementIntentClaimV1Aliases
{
    internal const string PlacementIntentClaimV1Alias = "keyload.cluster-routing.placement-intent-claim.v1";
}
