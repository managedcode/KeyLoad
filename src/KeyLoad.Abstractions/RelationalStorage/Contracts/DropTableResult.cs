namespace KeyLoad;

/// <summary>Reports the actual final catalog replicated commit after completed owner settlement and logical cleanup.</summary>
/// <param name="TenantId">The tenant scope.</param>
/// <param name="DatabaseId">The database scope.</param>
/// <param name="ResourceName">The resolved table name.</param>
/// <param name="Receipt">The original source catalog commit receipt; this does not promise physical erasure.</param>
[Orleans.GenerateSerializer, Orleans.Alias(DropTableContract.DropTableResultAlias)]
public sealed record DropTableResult(
    [property: Orleans.Id(0)] string TenantId,
    [property: Orleans.Id(1)] string DatabaseId,
    [property: Orleans.Id(2)] string ResourceName,
    [property: Orleans.Id(3)] CommitReceipt Receipt);
