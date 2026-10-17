namespace KeyLoad.Storage;

/// <summary>Describes a completed gate-scoped range visit without retaining record buffers.</summary>
/// <param name="Records">Number of logical records delivered to the visitor.</param>
/// <param name="HasMore">Whether a record-limit lookahead found another logical record.</param>
/// <param name="StoppedByVisitor">Whether the visitor explicitly stopped the traversal.</param>
/// <param name="ReadBytes">Examined raw key/value bytes, including charged lookahead work.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StorageScanResult)]
public readonly record struct StorageScanResult([property: Orleans.Id(0)] int Records, [property: Orleans.Id(1)] bool HasMore, [property: Orleans.Id(2)] bool StoppedByVisitor, [property: Orleans.Id(3)] long ReadBytes);
