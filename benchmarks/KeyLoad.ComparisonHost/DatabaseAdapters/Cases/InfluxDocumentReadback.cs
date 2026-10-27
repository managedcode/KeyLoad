using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

internal static class InfluxDocumentReadback
{
    private const int Empty = 0, Clients = 16, Records = 1_000_000, Single = 1, Updated = 1;

    internal static async Task VerifyAsync(IComparisonTarget target, CancellationToken token)
    {
        var documents = (IDocumentComparisonTarget)target;
        var corpus = new DocumentComparisonCorpus(Empty, Clients);
        await documents.InitializeDocumentsAsync(new(corpus, false, Records + Single), token);
        await NativeDatabaseTests.CreateBatchesAsync(documents, corpus, Records, token);
        await using var session = await documents.OpenDocumentSessionAsync(token);
        await session.ExecuteAsync(Scenario.DocumentUpdate, corpus.Replacement(Updated), token);
        await using var independent = await documents.OpenDocumentSessionAsync(token);
        var expected = Enumerable.Range(Empty, Records)
            .Select(index => index == Updated ? corpus.Replacement(index) : corpus.CreateDocument(index));
        var observed = await DocumentComparisonOracle.VerifyAsync(independent.ReadDocumentsAsync(token), expected, token);
        await Assert.That(observed.Records).IsEqualTo(Records);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using (var cursor = independent.ReadDocumentsAsync(cancelled.Token).GetAsyncEnumerator(cancelled.Token))
        {
            await Assert.That(await cursor.MoveNextAsync()).IsTrue();
            await cancelled.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => cursor.MoveNextAsync().AsTask());
        }
        await session.CreateDocumentsAsync([corpus.CreateDocument(Records)], token);
        await Assert.That(BenchmarkDataset.SameDocument((await independent.ReadAsync(corpus.CreateDocument(Records), token)).Value,
            corpus.CreateDocument(Records))).IsTrue();
    }
}
