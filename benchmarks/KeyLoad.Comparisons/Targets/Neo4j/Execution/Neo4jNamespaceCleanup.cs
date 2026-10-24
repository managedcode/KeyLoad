using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Deletes one run's nodes in bounded transactions, like an application purging a large namespace.</summary>
/// <remarks>Deleting a million-node namespace in one transaction exceeds the server's transaction memory pool.</remarks>
internal static class Neo4jNamespaceCleanup
{
    private const int CleanupBatchNodes = 10_000;
    private const long NoNodes = 0;
    private const int FirstElementIndex = 0;

    internal static async Task DeleteNodesAsync(Func<string, object?, CancellationToken, Task<JsonDocument>> query,
        string label, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var response = await query(Neo4jQueryRequests.DeleteNodeBatch(label, CleanupBatchNodes), null, cancellationToken);
            var deleted = DocumentNeo4jProtocol.Rows(response)[FirstElementIndex][FirstElementIndex].GetInt64();
            if (deleted == NoNodes)
            {
                return;
            }
        }
    }
}
