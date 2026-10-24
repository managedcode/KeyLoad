namespace KeyLoad.Query.QueryExecution;

internal sealed class DistributedSearchPreparedLeaf(DistributedSearchOwnedLeaf actualRequest,
    Func<CancellationToken, Task<DistributedSearchLeafResult>> run,
    Func<DistributedSearchLeafResult, CancellationToken, Task> complete)
{
    internal DistributedSearchOwnedLeaf ActualRequest { get; } = actualRequest;

    internal Task<DistributedSearchLeafResult> RunAsync(CancellationToken token) => run(token);

    internal Task CompleteAsync(DistributedSearchLeafResult result, CancellationToken token)
        => complete(result, token);
}
