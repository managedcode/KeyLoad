using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Checks native bulk acknowledgement, exact bodies and healthy continuation through the actual owned client.</summary>
[NotInParallel]
internal sealed class NativeDatabaseTests
{
    internal const string Redis = "Redis", OpenSearch = "OpenSearch", HelixDb = "HelixDB", MongoDb = "MongoDB";
    private const int Empty = 0, Clients = 16, First = 0, Updated = 1, Deleted = 2, Single = 1, ReplicaNodes = 3;
    private const int RedisRecords = 800_000, OpenSearchRecords = 1_000_000;
    private const string BodyMismatch = "NativeDatabaseBulkReadbackMismatch";

    [Test, Arguments(OpenSearch, 1), Arguments(OpenSearch, 3), Arguments(Redis, 3), Arguments(HelixDb, 1), Arguments(MongoDb, 1)]
    public Task NativeBulkPreservesBodiesAndIndependentClients(string target, int nodes)
        => AdditionalDatabaseResources.RunAsync(target, VerifyAsync, TestContext.Current!.Execution.CancellationToken, nodes);

    [Test]
    public Task MongoReplicaSetReadinessPrecedesIndependentBulkClients()
        => AdditionalDatabaseResources.RunAsync(MongoDb, VerifyAsync, TestContext.Current!.Execution.CancellationToken, ReplicaNodes);

    private static async Task VerifyAsync(IComparisonTarget target, CancellationToken token)
    {
        var documents = (IDocumentComparisonTarget)target;
        var corpus = new DocumentComparisonCorpus(Empty, Clients);
        var count = target.Profile.Name switch
        {
            Redis => RedisRecords,
            OpenSearch => OpenSearchRecords,
            _ => documents.IngestionBatchRecords * Clients
        };
        await documents.InitializeDocumentsAsync(new(corpus, false, count + Single), token);
        await using var session = await documents.OpenDocumentSessionAsync(token);
        await using var independent = await documents.OpenDocumentSessionAsync(token);
        await Assert.That(session.ClientIdentity).IsNotEqualTo(independent.ClientIdentity);
        await CreateBatchesAsync(documents, corpus, count, token);
        await session.ExecuteAsync(Scenario.DocumentUpdate, corpus.Replacement(Updated), token);
        await session.ExecuteAsync(Scenario.DocumentDelete, corpus.CreateDocument(Deleted), token);
        var observed = Empty;
        await foreach (var row in independent.ReadDocumentsAsync(token))
        {
            var ordinal = observed >= Deleted ? observed + Single : observed;
            var expected = ordinal == Updated ? corpus.Replacement(ordinal) : corpus.CreateDocument(ordinal);
            if (!BenchmarkDataset.SameDocument(row, expected))
            {
                throw new InvalidDataException(BodyMismatch);
            }
            observed++;
        }
        await Assert.That(observed).IsEqualTo(count - Single);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.ReadAsync(corpus.CreateDocument(First), cancelled.Token));
        await session.CreateDocumentsAsync([corpus.CreateDocument(count)], token);
        await Assert.That(BenchmarkDataset.SameDocument((await independent.ReadAsync(corpus.CreateDocument(count), token)).Value,
            corpus.CreateDocument(count))).IsTrue();
    }

    private static async Task CreateBatchesAsync(IDocumentComparisonTarget target, DocumentComparisonCorpus corpus,
        int count, CancellationToken token)
    {
        var sessions = new List<IDocumentComparisonSession>();
        Exception? primary = null;
        try
        {
            for (var client = First; client < Clients; client++)
            {
                sessions.Add(await target.OpenDocumentSessionAsync(token));
            }
            var batch = target.IngestionBatchRecords;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = sessions.Select(async (session, worker) =>
            {
                await start.Task;
                for (var first = worker * batch; first < count; first += Clients * batch)
                {
                    using var deadline = ComparisonDeadline.Create(BenchmarkSessionLimits.TimeoutSeconds, TimeProvider.System, token);
                    await session.CreateDocumentsAsync([.. Enumerable.Range(first, batch).Select(corpus.CreateDocument)], deadline.Token);
                }
            }).ToArray();
            start.SetResult();
            await Task.WhenAll(workers);
        }
        catch (Exception failure) when (BenchmarkSessionFailure.IsNonfatal(failure))
        {
            primary = failure;
        }
        var cleanup = await Task.WhenAll(sessions.Select(async session =>
            (await RuntimeComparisonFailure.ObserveAsync(session.DisposeAsync().AsTask())).Value));
        if ((RuntimeComparisonFailure.Combine(primary, [.. cleanup.OfType<Exception>()])).Value is { } combined)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
}
