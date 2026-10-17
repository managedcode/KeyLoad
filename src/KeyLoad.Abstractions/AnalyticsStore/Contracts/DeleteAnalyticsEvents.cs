using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Atomically tombstones the exact expected event revisions.</summary>
/// <param name="Store">Independent configured analytics store.</param>
/// <param name="ExpectedRevisions">Required event-to-revision CAS map.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Delete)]
public sealed record DeleteAnalyticsEvents(
    [property: Orleans.Id(0)] string Store,
    [property: Orleans.Id(1)] ImmutableDictionary<string, long> ExpectedRevisions) : Mutation(Store);
