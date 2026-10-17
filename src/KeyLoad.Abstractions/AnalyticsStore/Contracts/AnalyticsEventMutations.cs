using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Atomically inserts or replaces events using one required expected revision per event.</summary>
/// <param name="Store">Independent configured analytics store.</param>
/// <param name="Events">Complete replacement event values.</param>
/// <param name="ExpectedRevisions">Exact event-to-revision CAS map; zero means absent.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Upsert)]
public sealed record UpsertAnalyticsEvents(
    [property: Orleans.Id(0)] string Store,
    [property: Orleans.Id(1)] ImmutableArray<AnalyticsEventData> Events,
    [property: Orleans.Id(2)] ImmutableDictionary<string, long> ExpectedRevisions) : Mutation(Store);

/// <summary>Atomically tombstones the exact expected event revisions.</summary>
/// <param name="Store">Independent configured analytics store.</param>
/// <param name="ExpectedRevisions">Required event-to-revision CAS map.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Delete)]
public sealed record DeleteAnalyticsEvents(
    [property: Orleans.Id(0)] string Store,
    [property: Orleans.Id(1)] ImmutableDictionary<string, long> ExpectedRevisions) : Mutation(Store);
