using KeyLoad.ComparisonHost.Deployment;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>
/// MongoDB documents through MongoDB.Driver: <c>_id</c> is the document id and the JSON fields are stored as BSON, with an
/// ascending index on <c>number</c>. Writes use <c>w: majority, j: true</c>, reads use the primary with majority read
/// concern; the range query is one <c>find</c> on <c>number</c> between the bounds.
/// </summary>
/// <remarks>
/// The replica-set members advertise their container aliases, which only resolve inside the container network, so the
/// client connects directly to the published endpoint of the first member, the bootstrap's preferred primary.
/// </remarks>
internal sealed class MongoDocumentDriver : IDocumentDriver
{
    private const string DatabaseName = "benchmark";
    private const string CollectionName = "documents";
    private const string IdField = "_id";
    private const string NumberField = DocumentDataset.NumberProperty;
    private const string MissingDocument = "MongoDB has no document ";
    private const int Batch = 256;
    private const int PreferredPrimary = 0;
    private const long One = 1;
    private static readonly JsonWriterSettings PlainJson = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };
    private static readonly ProjectionDefinition<BsonDocument, BsonDocument> WithoutId = Builders<BsonDocument>.Projection.Exclude(IdField);
    private static readonly ProjectionDefinition<BsonDocument, BsonDocument> OnlyId = Builders<BsonDocument>.Projection.Include(IdField);
    private readonly MongoClient client;
    private readonly IMongoDatabase database;
    private readonly IMongoCollection<BsonDocument> collection;

    internal MongoDocumentDriver(BenchmarkConnection connection)
    {
        client = new(Settings(connection));
        database = client.GetDatabase(DatabaseName);
        collection = database.GetCollection<BsonDocument>(CollectionName);
    }

    public int BatchSize => Batch;

    public async Task PrepareAsync(CancellationToken token)
    {
        await database.CreateCollectionAsync(CollectionName, cancellationToken: token);
        var index = new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending(NumberField));
        await collection.Indexes.CreateOneAsync(index, cancellationToken: token);
    }

    public Task InsertAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken token)
        => collection.InsertManyAsync(documents.Select(Bson), cancellationToken: token);

    public async Task<string> ReadAsync(string id, CancellationToken token)
    {
        var stored = await collection.Find(ById(id)).Project(WithoutId).FirstOrDefaultAsync(token);
        return stored?.ToJson(PlainJson) ?? throw new KeyNotFoundException(MissingDocument + id);
    }

    public async Task<IReadOnlyList<string>> FindRangeAsync(long from, long to, CancellationToken token)
    {
        var filter = Builders<BsonDocument>.Filter.Gte(NumberField, from) & Builders<BsonDocument>.Filter.Lte(NumberField, to);
        var rows = await collection.Find(filter).Project(OnlyId).ToListAsync(token);
        return [.. rows.Select(row => row[IdField].AsString)];
    }

    public async Task ReplaceAsync(DocumentRecord document, CancellationToken token)
    {
        var result = await collection.ReplaceOneAsync(ById(document.Id), Bson(document), new ReplaceOptions { IsUpsert = false }, token);
        if (result.MatchedCount != One)
        {
            throw new KeyNotFoundException(MissingDocument + document.Id);
        }
    }

    public async Task DeleteAsync(string id, CancellationToken token)
    {
        var result = await collection.DeleteOneAsync(ById(id), token);
        if (result.DeletedCount != One)
        {
            throw new KeyNotFoundException(MissingDocument + id);
        }
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static FilterDefinition<BsonDocument> ById(string id) => Builders<BsonDocument>.Filter.Eq(IdField, id);

    private static BsonDocument Bson(DocumentRecord document)
        => new BsonDocument(IdField, document.Id).AddRange(BsonDocument.Parse(document.Json));

    private static MongoClientSettings Settings(BenchmarkConnection connection)
    {
        var settings = MongoClientSettings.FromConnectionString(connection.RequireConnectionString());
        var primary = connection.Endpoints[PreferredPrimary];
        settings.Servers = [new MongoServerAddress(primary.Host, primary.Port)];
        settings.DirectConnection = true;
        settings.WriteConcern = WriteConcern.WMajority.With(journal: true);
        settings.ReadConcern = ReadConcern.Majority;
        settings.ReadPreference = ReadPreference.Primary;
        return settings;
    }
}
