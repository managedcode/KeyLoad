using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Events;

/// <summary>The only operations an event store implements; thread-safe, shared by all clients.</summary>
internal interface IEventDriver : IAsyncDisposable
{
    /// <summary>Events per <see cref="AppendManyAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the stream set or store.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Appends events that each start a new stream; returns after the store acknowledged all of them.</summary>
    Task AppendManyAsync(IReadOnlyList<EventRecord> events, CancellationToken token);

    /// <summary>Appends one event to a new stream, expecting that the stream does not exist.</summary>
    Task AppendAsync(EventRecord record, CancellationToken token);

    /// <summary>Reads the first event of a stream; throws <see cref="KeyNotFoundException"/> when the stream is missing.</summary>
    Task<EventRecord> ReadFirstAsync(string stream, CancellationToken token);
}

/// <summary>Creates the event store driver of the deployed database.</summary>
internal static class EventDrivers
{
    internal static IEventDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadEventDriver(connection),
        BenchmarkDatabases.Kurrent => new KurrentEventDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Events + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
