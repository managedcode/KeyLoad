using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>
/// KeyLoad key-value through the public SDK: a <see cref="ResourceKind.Collection"/> without indexes whose document id is
/// the key and whose body is <c>{"v": value}</c>; writes are unconditional <see cref="PutDocument"/> and
/// <see cref="DeleteDocument"/> commits, reads are document point reads.
/// </summary>
internal sealed class KeyLoadKeyValueDriver(BenchmarkConnection connection) : IKeyValueDriver
{
    private const string Collection = "values";
    private const string ValueProperty = "v";
    private const string ReadOperation = "get";
    private const string MissingKey = "KeyLoad has no key ";
    private const string MissingValue = "KeyLoad stored no string value for key ";
    private const int Batch = 256;
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(Collection, ResourceKind.Collection), token);

    public Task SetManyAsync(IReadOnlyList<KeyValueRecord> records, CancellationToken token)
        => database.CommitAsync([.. records.Select(Put)], token);

    public async Task<string> GetAsync(string key, CancellationToken token)
    {
        var read = KeyLoadDatabase.Require(await database.Client.GetAsync(database.Entity(Collection, key), token), ReadOperation);
        var json = read.Document?.Json ?? throw new KeyNotFoundException(MissingKey + key);
        using var body = JsonDocument.Parse(json);
        return body.RootElement.GetProperty(ValueProperty).GetString() ?? throw new InvalidDataException(MissingValue + key);
    }

    public Task SetAsync(KeyValueRecord record, CancellationToken token)
        => database.CommitAsync([Put(record)], token);

    public Task DeleteAsync(string key, CancellationToken token)
        => database.CommitAsync([new DeleteDocument(Collection, key)], token);

    public ValueTask DisposeAsync() => database.DisposeAsync();

    /// <summary>An unconditional write: it creates the key or replaces its current value.</summary>
    private static Mutation Put(KeyValueRecord record)
        => new PutDocument(Collection, record.Key, new JsonObject { [ValueProperty] = record.Value }.ToJsonString(),
            ExplicitReplacement: true);
}
