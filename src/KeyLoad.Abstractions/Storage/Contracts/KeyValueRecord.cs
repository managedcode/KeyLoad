namespace KeyLoad.Storage;

/// <summary>Owns one independent key and value copied from a gated storage view.</summary>
/// <param name="Key">Encoded record key owned by this result.</param>
/// <param name="Value">Logical record bytes owned by this result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.KeyValueRecord)]
public sealed record KeyValueRecord([property: Orleans.Id(0)] ReadOnlyMemory<byte> Key, [property: Orleans.Id(1)] ReadOnlyMemory<byte> Value);
