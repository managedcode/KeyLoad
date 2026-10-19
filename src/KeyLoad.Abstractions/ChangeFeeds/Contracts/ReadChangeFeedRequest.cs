namespace KeyLoad;

/// <summary>Requests a bounded page from a collection change feed.</summary>
/// <param name="Partition">The partition containing the collection.</param>
/// <param name="Collection">The collection to read changes for.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
/// <param name="Start">The starting point when no cursor is supplied.</param>
/// <param name="Limit">The maximum number of changes to return.</param>
/// <param name="MaxBytes">The maximum page size in bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ReadChangeFeedRequest)]
public sealed record ReadChangeFeedRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string? Cursor = null,
    [property: Orleans.Id(3)] ChangeFeedStart Start = ChangeFeedStart.Beginning, [property: Orleans.Id(4)] int Limit = ReadChangeFeedRequest.DefaultLimit, [property: Orleans.Id(5)] int MaxBytes = ReadChangeFeedRequest.DefaultMaxBytes)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}
