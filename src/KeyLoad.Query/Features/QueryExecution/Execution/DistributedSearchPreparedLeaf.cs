namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class DistributedSearchPreparedLeaf(DistributedSearchOwnedLeafV1 actualRequest,
    Func<CancellationToken, Task<DistributedSearchLeafResultV1>> run,
    Func<DistributedSearchLeafResultV1, CancellationToken, Task> complete)
{
    internal DistributedSearchOwnedLeafV1 ActualRequest { get; } = actualRequest;

    internal Task<DistributedSearchLeafResultV1> RunAsync(CancellationToken token) => run(token);

    internal Task CompleteAsync(DistributedSearchLeafResultV1 result, CancellationToken token)
        => complete(result, token);
}
