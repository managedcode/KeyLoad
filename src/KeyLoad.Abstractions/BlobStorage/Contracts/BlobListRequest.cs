namespace KeyLoad;

/// <summary>Requests a bounded ordered listing of authorized live object metadata.</summary>
/// <param name="Partition">The complete atomic partition scope.</param>
/// <param name="Resource">The configured binary resource.</param>
/// <param name="Limit">The maximum returned visible metadata rows.</param>
/// <param name="AfterId">The optional exclusive last-visited object identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobListRequest)]
public sealed record BlobListRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Resource,
    [property: Orleans.Id(2)] int Limit = BlobLimits.MaxListItems, [property: Orleans.Id(3)] string? AfterId = null);
