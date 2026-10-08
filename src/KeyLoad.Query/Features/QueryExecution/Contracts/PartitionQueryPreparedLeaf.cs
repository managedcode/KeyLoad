namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Separates one actual bounded native child from serialized parent accounting.</summary>
internal sealed class PartitionQueryPreparedLeaf(
    Func<CancellationToken, Task<PartitionQueryOwnedLeaf>> run,
    Func<PartitionQueryOwnedLeaf, CancellationToken, Task> complete)
{
    internal Task<PartitionQueryOwnedLeaf> RunAsync(CancellationToken token) => run(token);
    internal Task CompleteAsync(PartitionQueryOwnedLeaf result, CancellationToken token) => complete(result, token);
}
