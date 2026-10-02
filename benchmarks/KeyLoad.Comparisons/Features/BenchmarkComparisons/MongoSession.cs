using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed class MongoSession(MongoTarget target, IMongoCollection<BsonDocument> documents,
    IMongoCollection<BsonDocument> edges, IMongoCollection<BsonDocument> events, int graphDepth) : IComparisonSession
{
    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var stored = await documents.Find(IdFilter(document.Id)).FirstOrDefaultAsync(cancellationToken);
        return stored is null ? null : new FoundDocument(document.Id, stored.GetValue(MongoSchema.BodyField).AsString);
    }

    public async Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var stored = await events.Find(IdFilter(target.StreamName(document))).Limit(MongoSchema.DuplicateProbeCount)
            .ToListAsync(cancellationToken);
        if (stored.Count != MongoSchema.SingleEventCount)
        {
            throw new ComparisonFailureException(MongoSchema.FailureReadCardinality);
        }
        var result = new FoundEvent(Guid.Parse(stored[0].GetValue(MongoSchema.EventIdField).AsString),
            checked((ulong)stored[0].GetValue(MongoSchema.RevisionField).ToInt64()),
            stored[0].GetValue(MongoSchema.JsonField).AsString);
        return result;
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        switch (scenario)
        {
            case Scenario.PointRead:
                return new(Document: await ReadAsync(document, cancellationToken));
            case Scenario.DocumentWrite:
                await documents.InsertOneAsync(MongoSchema.Document(document), cancellationToken: cancellationToken);
                return new();
            case Scenario.GraphNeighbors:
            case Scenario.GraphTraverse:
                return new(Vertices: await MongoGraphRead.ReadAsync(documents, edges, document,
                    scenario == Scenario.GraphNeighbors ? MongoSchema.GraphBaseDepth : graphDepth, cancellationToken));
            case Scenario.StreamAppend:
                await events.InsertOneAsync(MongoSchema.Event(target.StreamName(document),
                    BenchmarkDataset.EventId(document), document.Json), cancellationToken: cancellationToken);
                return new();
            case Scenario.StreamRead:
                return new(Event: await ReadEventAsync(document, cancellationToken));
            default:
                throw new ComparisonFailureException(MongoSchema.FailureUnsupportedScenario);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static FilterDefinition<BsonDocument> IdFilter(string id)
        => Builders<BsonDocument>.Filter.Eq(MongoSchema.IdField, id);
}
