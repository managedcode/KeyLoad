namespace KeyLoad;

/// <summary>Requests a bounded raw range at one required current object revision.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The positive revision required for this read cut.</param>
/// <param name="Offset">The zero-based raw byte offset, including an empty EOF range.</param>
/// <param name="Count">The exact bounded number of requested bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReadRequest)]
public sealed record BlobReadRequest([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] long ExpectedRevision, [property: Orleans.Id(2)] long Offset, [property: Orleans.Id(3)] int Count);
