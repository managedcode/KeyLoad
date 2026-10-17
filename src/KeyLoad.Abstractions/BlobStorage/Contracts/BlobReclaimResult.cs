namespace KeyLoad;

/// <summary>Returns logical deletion progress without reclaiming a current version.</summary>
/// <param name="UploadId">The selected upload/version identity.</param>
/// <param name="DeletedParts">The parts logically deleted in this command.</param>
/// <param name="RemainingParts">The remaining accepted parts.</param>
/// <param name="ReleasedBytes">The unused reservation and deleted logical bytes released now.</param>
/// <param name="Complete">Whether this version has no remaining state or parts.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReclaimResult)]
public sealed record BlobReclaimResult([property: Orleans.Id(0)] Guid UploadId, [property: Orleans.Id(1)] int DeletedParts, [property: Orleans.Id(2)] int RemainingParts,
    [property: Orleans.Id(3)] long ReleasedBytes, [property: Orleans.Id(4)] bool Complete);
