namespace KeyLoad;

/// <summary>Describes the current tail, retention floor, and generation of a stream.</summary>
/// <param name="TailRevision">Identifies the current stream tail.</param>
/// <param name="FirstAvailableRevision">Identifies the earliest retained revision.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StreamHead)]
public sealed record StreamHead([property: Orleans.Id(0)] long TailRevision, [property: Orleans.Id(1)] long FirstAvailableRevision, [property: Orleans.Id(2)] long Generation);
