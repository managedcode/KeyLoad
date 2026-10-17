namespace KeyLoad;

/// <summary>Publishes a revision tombstone while retaining bytes for bounded reclaim.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The required positive current head revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeleteBlobRequest)]
public sealed record DeleteBlobRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] long ExpectedRevision);
