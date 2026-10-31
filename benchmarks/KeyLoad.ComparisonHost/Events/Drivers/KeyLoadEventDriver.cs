using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>
/// KeyLoad stream set through the KeyLoad SDK: <see cref="AppendEvents"/> mutations with
/// <see cref="ExpectedStreamRevision.NoStream"/> (one commit per append, one commit of many streams per load batch)
/// and forward <c>ReadStreamAsync</c> pages from the stream start.
/// </summary>
internal sealed class KeyLoadEventDriver(BenchmarkConnection connection) : IEventDriver
{
    private const string StreamSet = "events";
    private const int Batch = 100;
    private const long BeforeFirstRevision = 0;
    private const int ReadLimit = 2;
    private const string MissingStream = "KeyLoad stream not found: ";
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(StreamSet, ResourceKind.StreamSet), token);

    public Task AppendManyAsync(IReadOnlyList<EventRecord> events, CancellationToken token)
        => database.CommitAsync([.. events.Select(Append)], token);

    public Task AppendAsync(EventRecord record, CancellationToken token)
        => database.CommitAsync([Append(record)], token);

    public async Task<EventRecord> ReadFirstAsync(string stream, CancellationToken token)
    {
        var request = new ReadStreamRequest(new StreamRef(database.Partition, StreamSet, stream),
            AfterRevision: BeforeFirstRevision, Limit: ReadLimit);
        var page = KeyLoadDatabase.Require(await database.Client.ReadStreamAsync(request, token), nameof(ReadFirstAsync));
        if (page.Events.IsDefaultOrEmpty)
        {
            throw new KeyNotFoundException(MissingStream + stream);
        }
        var first = page.Events[0].Data;
        return new(stream, Guid.Parse(first.EventId), first.EventType, first.PayloadJson);
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private static Mutation Append(EventRecord record)
        => new AppendEvents(StreamSet, record.Stream, [new EventData(record.EventId.ToString("D"), record.Type, record.Json)],
            ExpectedStreamRevision.NoStream);
}
