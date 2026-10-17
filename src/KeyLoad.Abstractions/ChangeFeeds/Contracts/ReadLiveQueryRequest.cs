namespace KeyLoad;

/// <summary>Requests a bounded page of changes for a live query.</summary>
/// <param name="Query">The typed query being observed.</param>
/// <param name="Cursor">The cursor returned by the snapshot or a prior page.</param>
/// <param name="Limit">The maximum number of changes to return.</param>
/// <param name="MaxBytes">The maximum page size in bytes.</param>
/// <remarks>Live query deltas are bounded and unordered; ordering and top-k use separate query profiles.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadLiveQueryRequest)]
public sealed record ReadLiveQueryRequest([property: Orleans.Id(0)] KeyLoad.Query.AstQueryRequest Query, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] int Limit = ReadLiveQueryRequest.DefaultLimit, [property: Orleans.Id(3)] int MaxBytes = ReadLiveQueryRequest.DefaultMaxBytes)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}
