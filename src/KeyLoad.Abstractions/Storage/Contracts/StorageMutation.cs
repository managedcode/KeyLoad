namespace KeyLoad.Storage;

/// <summary>Describes an ordered atomic record write or tombstone.</summary>
/// <param name="Key">Encoded record key included in the commit.</param>
/// <param name="Value">New logical bytes, or null to delete the key.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StorageMutation)]
public sealed record StorageMutation([property: Orleans.Id(0)] ReadOnlyMemory<byte> Key, [property: Orleans.Id(1)] ReadOnlyMemory<byte>? Value);
