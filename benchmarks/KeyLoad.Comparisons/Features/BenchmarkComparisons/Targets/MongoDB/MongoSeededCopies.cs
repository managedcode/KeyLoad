using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoSeededCopies
{
    internal static async Task VerifyAsync(IEnumerable<IMongoClient> clients, string databaseName, IComparisonCorpus dataset,
        IOptions<ComparisonLifecycleOptions> lifecycleOptions, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(dataset.Settings.TimeoutSeconds));
        try
        {
            foreach (var client in clients)
            {
                await WaitForCopyAsync(collection: client.GetDatabase(databaseName).GetCollection<BsonDocument>(MongoSchema.DocumentsCollection),
                    expected: dataset.Documents[FirstElementIndex], count: dataset.Documents.Count, cancellationToken: deadline.Token,
                    pollInterval: lifecycleOptions.Value.MongoReadinessPollInterval);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(MongoSchema.FailureCopyProbeTimeout);
        }
    }

    private static async Task WaitForCopyAsync(IMongoCollection<BsonDocument> collection, BenchmarkDocument expected, long count,
        TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (true)
        {
            var found = await collection.Find(new BsonDocument(MongoSchema.IdField, expected.Id)).FirstOrDefaultAsync(cancellationToken);
            var observedCount = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
            if (found is not null && observedCount == count)
            {
                if (found[MongoSchema.BodyField].AsString != expected.Json)
                {
                    throw new ComparisonFailureException(MongoSchema.FailureDataCopyMissing);
                }
                return;
            }
            await Task.Delay(pollInterval, cancellationToken);
        }
    }
}
