using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains outbox entries returned to a projection consumer.</summary>
/// <param name="Consumer">The consumer status for this batch.</param>
/// <param name="Entries">The entries in the batch.</param>
/// <param name="ThroughSequence">The final sequence included in the batch.</param>
/// <param name="Token">The token used to acknowledge the batch.</param>
/// <param name="HasMore">Whether another entry is available.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionBatch)]
public sealed record ProjectionBatch([property: Orleans.Id(0)] ProjectionConsumerInfo Consumer, [property: Orleans.Id(1)] ImmutableArray<OutboxEntry> Entries, [property: Orleans.Id(2)] long ThroughSequence,
    [property: Orleans.Id(3)] string Token, [property: Orleans.Id(4)] bool HasMore);
