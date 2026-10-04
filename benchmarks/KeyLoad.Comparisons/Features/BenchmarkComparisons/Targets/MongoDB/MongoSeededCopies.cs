using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoSeededCopies
{
    internal static async Task VerifyAsync(IEnumerable<IMongoClient> clients, string databaseName,
        BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(dataset.Options.TimeoutSeconds));
        try
        {
            foreach (var client in clients)
            {
                await WaitForCopyAsync(client.GetDatabase(databaseName).GetCollection<BsonDocument>(MongoSchema.DocumentsCollection),
                    dataset.Documents[0], dataset.Documents.Length, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(MongoSchema.FailureCopyProbeTimeout);
        }
    }

    private static async Task WaitForCopyAsync(IMongoCollection<BsonDocument> collection, BenchmarkDocument expected,
        long count, CancellationToken cancellationToken)
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
            await Task.Delay(TimeSpan.FromMilliseconds(MongoSchema.ProbePollMilliseconds), cancellationToken);
        }
    }
}
