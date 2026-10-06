using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoCorpusSeed
{
    public static async Task SeedAsync(IComparisonCorpus dataset, IMongoCollection<BsonDocument> documents,
        IMongoCollection<BsonDocument> edges, IMongoCollection<BsonDocument> events,
        Func<BenchmarkDocument, string> streamName, IOptions<NativeComparisonExecutionOptions> executionOptions,
        CancellationToken cancellationToken)
    {
        var batchSize = NativeComparisonExecutionOptions.Require(executionOptions).Value.MongoSeedBatchSize;
        await InsertBatchesAsync(dataset.Documents.Select(MongoSchema.Document), documents, batchSize, cancellationToken);
        if (dataset is BenchmarkDataset)
        {
            await InsertBatchesAsync(dataset.Edges.Select(CreateEdge), edges, batchSize, cancellationToken);
            var streamEvents = dataset.Documents.Select(document => MongoSchema.Event(streamName(document),
                BenchmarkDataset.EventId(document), document.Json));
            await InsertBatchesAsync(streamEvents, events, batchSize, cancellationToken);
        }
    }

    private static BsonDocument CreateEdge(BenchmarkEdge edge)
        => new() { [MongoSchema.IdField] = edge.Id, [MongoSchema.FromField] = edge.From, [MongoSchema.ToField] = edge.To };

    private static async Task InsertBatchesAsync(IEnumerable<BsonDocument> values,
        IMongoCollection<BsonDocument> collection, int batchSize, CancellationToken cancellationToken)
    {
        foreach (var batch in values.Chunk(batchSize))
        {
            await collection.InsertManyAsync(batch, cancellationToken: cancellationToken);
        }
    }
}
