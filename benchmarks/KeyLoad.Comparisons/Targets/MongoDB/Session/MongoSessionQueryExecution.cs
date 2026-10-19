using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoSessionQueryExecution
{
    private const string CollectionUnavailable = "SessionDocumentCollectionUnavailable";
    private const string BodyHashIndex = "session_body_hash";
    private const string IndexIdentityMismatch = "SessionIndexIdentityMismatch";
    private const string QueryCardinalityMismatch = "SessionQueryCardinalityMismatch";
    private const int NextDocumentOffset = 1;
    private const int QueryLookaheadRows = 2;
    private const int ExpectedRows = 1;
    private const int FirstRow = 0;

    internal static async Task BuildIndexesAsync(IMongoCollection<BsonDocument>? documents,
        CancellationToken cancellationToken)
    {
        if (documents is null)
        {
            throw new InvalidOperationException(CollectionUnavailable);
        }
        var model = new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Hashed(MongoSchema.BodyField),
            new CreateIndexOptions { Name = BodyHashIndex });
        var name = await documents.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (name != BodyHashIndex)
        {
            throw new ComparisonFailureException(IndexIdentityMismatch);
        }
    }

    internal static async Task QueryAsync(IMongoCollection<BsonDocument>? documents, int ordinal,
        int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        if (documents is null)
        {
            throw new InvalidOperationException(CollectionUnavailable);
        }
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var next = ScaledComparisonCorpus.Id(expected.Number + NextDocumentOffset);
        var filter = Builders<BsonDocument>.Filter.Eq(MongoSchema.IdField, expected.Id)
            & Builders<BsonDocument>.Filter.Gte(MongoSchema.IdField, expected.Id)
            & Builders<BsonDocument>.Filter.Lt(MongoSchema.IdField, next);
        var rows = await documents.Find(filter).Limit(QueryLookaheadRows).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != ExpectedRows)
        {
            throw new ComparisonFailureException(QueryCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(rows[FirstRow][MongoSchema.IdField].AsString,
            rows[FirstRow][MongoSchema.BodyField].AsString), expected);
    }
}
