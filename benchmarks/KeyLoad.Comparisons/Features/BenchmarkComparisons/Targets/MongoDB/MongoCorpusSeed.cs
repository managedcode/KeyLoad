using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoCorpusSeed
{
    private const int BatchSize = 256;

    public static async Task SeedAsync(IComparisonCorpus dataset, IMongoCollection<BsonDocument> documents,
        IMongoCollection<BsonDocument> edges, IMongoCollection<BsonDocument> events,
        Func<BenchmarkDocument, string> streamName, CancellationToken cancellationToken)
    {
        await InsertBatchesAsync(dataset.Documents.Select(MongoSchema.Document), documents, cancellationToken);
        if (dataset is BenchmarkDataset)
        {
            await InsertBatchesAsync(dataset.Edges.Select(CreateEdge), edges, cancellationToken);
            var streamEvents = dataset.Documents.Select(document => MongoSchema.Event(streamName(document),
                BenchmarkDataset.EventId(document), document.Json));
            await InsertBatchesAsync(streamEvents, events, cancellationToken);
        }
    }

    private static BsonDocument CreateEdge(BenchmarkEdge edge)
        => new() { [MongoSchema.IdField] = edge.Id, [MongoSchema.FromField] = edge.From, [MongoSchema.ToField] = edge.To };

    private static async Task InsertBatchesAsync(IEnumerable<BsonDocument> values,
        IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        foreach (var batch in values.Chunk(BatchSize))
        {
            await collection.InsertManyAsync(batch, cancellationToken: cancellationToken);
        }
    }
}
