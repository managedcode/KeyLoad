using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoSeededCopies
{
    internal static async Task VerifyAsync(IEnumerable<IMongoClient> clients, string databaseName,
        IComparisonCorpus dataset, CancellationToken cancellationToken, IOptions<ComparisonLifecycleOptions> lifecycleOptions)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(dataset.Settings.TimeoutSeconds));
        try
        {
            foreach (var client in clients)
            {
                await WaitForCopyAsync(client.GetDatabase(databaseName).GetCollection<BsonDocument>(MongoSchema.DocumentsCollection),
                    dataset.Documents[0], dataset.Documents.Count, deadline.Token, lifecycleOptions.Value.MongoReadinessPollInterval);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(MongoSchema.FailureCopyProbeTimeout);
        }
    }

    private static async Task WaitForCopyAsync(IMongoCollection<BsonDocument> collection, BenchmarkDocument expected,
        long count, CancellationToken cancellationToken, TimeSpan pollInterval)
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
