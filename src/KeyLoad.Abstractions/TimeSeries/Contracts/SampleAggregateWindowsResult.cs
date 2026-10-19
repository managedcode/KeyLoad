using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Owns the dense ascending UTC windows returned by one complete operation.</summary>
/// <param name="Windows">Immutable windows, including empty windows, serialized as a JSON array.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SampleAggregateWindowsResult)]
public sealed record SampleAggregateWindowsResult([property: Orleans.Id(0)] ImmutableArray<SampleAggregateWindow> Windows);
