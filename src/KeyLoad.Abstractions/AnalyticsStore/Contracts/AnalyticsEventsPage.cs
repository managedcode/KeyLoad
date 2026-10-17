using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Owns the complete bounded event page; continuation is explicit when more authorized rows exist.</summary>
/// <param name="Events">Authorized rows ordered by ordinal event ID.</param>
/// <param name="HasMore">Whether another authorized event remains.</param>
/// <param name="NextAfterId">Exclusive continuation only when more rows remain.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Page)]
public sealed record AnalyticsEventsPage(
    [property: Orleans.Id(0)] ImmutableArray<AnalyticsEventRecord> Events,
    [property: Orleans.Id(1)] bool HasMore,
    [property: Orleans.Id(2)] string? NextAfterId);
