using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Reports the outbox head and configured projection consumers.</summary>
/// <param name="Head">The outbox retention and storage summary.</param>
/// <param name="Consumers">The projection consumers in the partition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.OutboxStatus)]
public sealed record OutboxStatus([property: Orleans.Id(0)] OutboxHead Head, [property: Orleans.Id(1)] ImmutableArray<ProjectionConsumerInfo> Consumers);
