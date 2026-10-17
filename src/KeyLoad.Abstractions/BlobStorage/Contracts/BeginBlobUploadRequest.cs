namespace KeyLoad;

/// <summary>Reserves bounded capacity and starts one scoped upload lifetime.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The scoped upload identity, separate from command identity.</param>
/// <param name="Length">The declared complete raw byte length.</param>
/// <param name="ExpectedRevision">The required existing head revision, zero for a new name.</param>
/// <param name="Access">Optional row access, otherwise inherited from the head.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BeginBlobUploadRequest)]
public sealed record BeginBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] long Length, [property: Orleans.Id(4)] long ExpectedRevision, [property: Orleans.Id(5)] RowAccess? Access = null);
