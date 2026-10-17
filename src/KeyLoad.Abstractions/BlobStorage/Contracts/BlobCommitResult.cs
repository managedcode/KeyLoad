namespace KeyLoad;

/// <summary>Returns one canonical commit receipt and its bounded feature result.</summary>
/// <typeparam name="T">The bounded public blob result.</typeparam>
/// <param name="Receipt">The existing ordered commit identity, token and durability.</param>
/// <param name="Value">The upload, metadata or cleanup result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobCommitResult)]
public sealed record BlobCommitResult<T>([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] T Value);
