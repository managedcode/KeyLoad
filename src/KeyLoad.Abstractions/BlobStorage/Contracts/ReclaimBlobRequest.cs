namespace KeyLoad;

/// <summary>Reclaims bounded parts of a selected expired, aborted or retired version.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The selected version identity.</param>
/// <param name="MaxParts">The inclusive deletion limit for this command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReclaimBlobRequest)]
public sealed record ReclaimBlobRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] int MaxParts = BlobLimits.MaxReclaimParts);
