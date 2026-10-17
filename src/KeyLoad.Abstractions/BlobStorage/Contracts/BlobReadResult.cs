namespace KeyLoad;

/// <summary>Returns exact requested bytes and metadata from one scoped read cut.</summary>
/// <param name="Metadata">The authorized required head revision.</param>
/// <param name="Offset">The requested raw starting offset.</param>
/// <param name="Bytes">The independent bounded raw range.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReadResult)]
public sealed record BlobReadResult([property: Orleans.Id(0)] BlobMetadata Metadata, [property: Orleans.Id(1)] long Offset, [property: Orleans.Id(2)] ReadOnlyMemory<byte> Bytes);
