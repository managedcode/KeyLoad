using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Exact persisted queue counters plus a bounded metadata display page.</summary>
/// <param name="Counters">Persisted storage/in-flight counters; no inferred ready/DLQ total.</param>
/// <param name="Items">Queue metadata records in canonical key order.</param>
/// <param name="NextAfterId">Exclusive metadata continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminQueuePage)]
public sealed record AdminQueuePage([property: Orleans.Id(0)] QueueCounters Counters, [property: Orleans.Id(1)] ImmutableArray<AdminQueueItem> Items,
    [property: Orleans.Id(2)] string? NextAfterId, [property: Orleans.Id(3)] long CutPosition);
