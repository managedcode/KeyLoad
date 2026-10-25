namespace KeyLoad.Comparisons;

internal static class BenchmarkSessionDocumentOperation
{
    internal static async Task ExecuteAsync(IComparisonSession? client, Scenario operation, BenchmarkDocument document, CancellationToken token)
    {
        var output = await (client ?? throw new InvalidOperationException(BenchmarkSessionProtocol.DocumentSessionRequired)).ExecuteAsync(operation, document, token).ConfigureAwait(false);
        if (operation == Scenario.PointRead && !BenchmarkDataset.SameDocument(output.Document, document))
        {
            throw new ComparisonFailureException(BenchmarkSessionProtocol.BenchmarkSessionReadMismatch);
        }
    }
}
