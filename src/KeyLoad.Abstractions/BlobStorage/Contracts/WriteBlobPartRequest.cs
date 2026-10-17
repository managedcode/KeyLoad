namespace KeyLoad;

/// <summary>Appends or identically repeats one bounded raw part.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="Ordinal">The zero-based ordered part position.</param>
/// <param name="Bytes">The raw part bytes serialized using strict base64.</param>
/// <param name="Sha256">The canonical lowercase SHA256 of these raw bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.WriteBlobPartRequest)]
public sealed record WriteBlobPartRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] int Ordinal, [property: Orleans.Id(4)] ReadOnlyMemory<byte> Bytes, [property: Orleans.Id(5)] string Sha256);
