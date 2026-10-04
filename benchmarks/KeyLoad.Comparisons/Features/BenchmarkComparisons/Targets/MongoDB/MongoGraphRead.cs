using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoGraphRead
{
    private const int MatchStageCount = 6;

    public static async Task<ImmutableArray<string>> ReadAsync(IMongoCollection<BsonDocument> documents,
        IMongoCollection<BsonDocument> edges, BenchmarkDocument start, int depth, CancellationToken cancellationToken)
    {
        var pipeline = BuildPipeline(edges.CollectionNamespace.CollectionName, start.Id, depth);
        using var cursor = await documents.AggregateAsync(pipeline, cancellationToken: cancellationToken);
        var ids = new List<string>();
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            ids.AddRange(cursor.Current.Select(result => result.GetValue(MongoSchema.GraphGroupId).AsString));
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(ids.Order(StringComparer.Ordinal).ToArray());
    }

    private static PipelineDefinition<BsonDocument, BsonDocument> BuildPipeline(string edgeCollection,
        string startId, int depth)
    {
        var stages = new BsonDocument[MatchStageCount];
        stages[0] = new BsonDocument(MongoSchema.GraphMatchOperator, new BsonDocument(MongoSchema.IdField, startId));
        stages[1] = new BsonDocument(MongoSchema.GraphLookupOperator, new BsonDocument
        {
            [MongoSchema.GraphFromCollection] = edgeCollection,
            [MongoSchema.GraphStartField] = startId,
            [MongoSchema.GraphConnectFromField] = MongoSchema.ToField,
            [MongoSchema.GraphConnectToField] = MongoSchema.FromField,
            [MongoSchema.GraphMaxDepthField] = depth - MongoSchema.GraphBaseDepth,
            [MongoSchema.GraphAsField] = MongoSchema.GraphOutputField
        });
        stages[2] = new BsonDocument(MongoSchema.GraphUnwindOperator, MongoSchema.GraphEdgePrefix);
        stages[3] = new BsonDocument(MongoSchema.GraphMatchOperator,
            new BsonDocument(MongoSchema.GraphEdgeDestinationPath, new BsonDocument(MongoSchema.GraphNotEqualOperator, startId)));
        stages[4] = new BsonDocument(MongoSchema.GraphGroupOperator,
            new BsonDocument(MongoSchema.GraphGroupId, MongoSchema.GraphEdgeDestination));
        stages[5] = new BsonDocument(MongoSchema.GraphSortOperator, new BsonDocument(MongoSchema.GraphGroupId, MongoSchema.GraphSortDirection));
        return PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
    }
}
