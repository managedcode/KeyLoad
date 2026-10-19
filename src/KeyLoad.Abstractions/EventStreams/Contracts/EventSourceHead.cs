namespace KeyLoad;

/// <summary>Reports the retained range and current tail of an event source.</summary>
/// <param name="TailPosition">The latest position assigned by the source.</param>
/// <param name="FirstAvailablePosition">The earliest position still available for reading.</param>
/// <param name="Generation">The current source generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.EventSourceHead)]
public sealed record EventSourceHead([property: Orleans.Id(0)] long TailPosition, [property: Orleans.Id(1)] long FirstAvailablePosition, [property: Orleans.Id(2)] long Generation);
