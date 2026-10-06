using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed class MongoSession(MongoTarget target, IMongoCollection<BsonDocument> documents,
    IMongoCollection<BsonDocument> edges, IMongoCollection<BsonDocument> events, int graphDepth, int corpusCount, IOptions<NativeComparisonExecutionOptions> executionOptions) : IComparisonSession
{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const string ScaledCorpusReadbackExtraRecordDetail = "ScaledCorpusReadbackExtraRecord";
        const string ScaledCorpusReadbackCountMismatchDetail = "ScaledCorpusReadbackCountMismatch";

        var find = documents.Find(FilterDefinition<BsonDocument>.Empty,
                new FindOptions { BatchSize = execution.ReadbackBatchCapacity })
            .Sort(Builders<BsonDocument>.Sort.Ascending(MongoSchema.IdField));
        using var cursor = await find.ToCursorAsync(cancellationToken);
        var seen = NoObservedItems;
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var stored in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen >= corpusCount)
                {
                    throw new ComparisonFailureException(ScaledCorpusReadbackExtraRecordDetail);
                }
                yield return new(stored.GetValue(MongoSchema.IdField).AsString,
                    stored.GetValue(MongoSchema.BodyField).AsString);
                seen++;
            }
        }
        if (seen != corpusCount)
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackCountMismatchDetail);
        }
    }

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var stored = await documents.Find(IdFilter(document.Id)).FirstOrDefaultAsync(cancellationToken);
        return stored is null ? null : new FoundDocument(document.Id, stored.GetValue(MongoSchema.BodyField).AsString);
    }

    public async Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        var stored = await events.Find(IdFilter(target.StreamName(document))).Limit(MongoSchema.DuplicateProbeCount)
            .ToListAsync(cancellationToken);
        if (stored.Count != MongoSchema.SingleEventCount)
        {
            throw new ComparisonFailureException(MongoSchema.FailureReadCardinality);
        }
        var result = new FoundEvent(Guid.Parse(stored[FirstElementIndex].GetValue(MongoSchema.EventIdField).AsString),
            checked((ulong)stored[FirstElementIndex].GetValue(MongoSchema.RevisionField).ToInt64()),
            stored[FirstElementIndex].GetValue(MongoSchema.JsonField).AsString);
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
                await CreateAsync(document, cancellationToken);
                return new();
            case Scenario.DocumentUpdate:
                ValidateUpdateResult(await documents.UpdateOneAsync(IdFilter(document.Id),
                    Builders<BsonDocument>.Update.Set(MongoSchema.BodyField, document.Json),
                    new UpdateOptions { IsUpsert = false }, cancellationToken));
                return new();
            case Scenario.DocumentDelete:
                ValidateDeleteResult(await documents.DeleteOneAsync(IdFilter(document.Id), cancellationToken));
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

    private async Task CreateAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await documents.InsertOneAsync(MongoSchema.Document(document), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException error) when (error.WriteError.Code == MongoSchema.DuplicateKeyCode)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict, error);
        }
    }

    internal static void ValidateUpdateResult(UpdateResult result)
    {
        const int NoObservedItems = 0;
        const int SingleItemCount = 1;

        if (!result.IsAcknowledged || !result.IsModifiedCountAvailable || result.UpsertedId is not null)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        if (result.MatchedCount == NoObservedItems && result.ModifiedCount == NoObservedItems)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.UpdateMissing);
        }
        if (result.MatchedCount != SingleItemCount || result.ModifiedCount != SingleItemCount)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
    }

    internal static void ValidateDeleteResult(DeleteResult result)
    {
        const int SingleItemCount = 1;
        const int NoItems = 0;
        const int NoObservedItems = 0;

        if (!result.IsAcknowledged || result.DeletedCount > SingleItemCount || result.DeletedCount < NoItems)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
        if (result.DeletedCount == NoObservedItems)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.DeleteMissing);
        }
    }

    private static FilterDefinition<BsonDocument> IdFilter(string id)
        => Builders<BsonDocument>.Filter.Eq(MongoSchema.IdField, id);
}
