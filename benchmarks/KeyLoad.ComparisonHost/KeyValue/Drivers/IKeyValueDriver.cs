using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>The only operations a key-value store implements; thread-safe, shared by all clients.</summary>
internal interface IKeyValueDriver : IAsyncDisposable
{
    /// <summary>Keys per <see cref="SetManyAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the keyspace or collection before any key is written.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Writes many keys; returns after the store acknowledged them.</summary>
    Task SetManyAsync(IReadOnlyList<KeyValueRecord> records, CancellationToken token);

    /// <summary>Reads one value; throws <see cref="KeyNotFoundException"/> when the key is missing.</summary>
    Task<string> GetAsync(string key, CancellationToken token);

    /// <summary>Writes one key, replacing any value.</summary>
    Task SetAsync(KeyValueRecord record, CancellationToken token);

    /// <summary>Deletes one existing key.</summary>
    Task DeleteAsync(string key, CancellationToken token);
}

/// <summary>Creates the key-value driver of the deployed database.</summary>
internal static class KeyValueDrivers
{
    internal static IKeyValueDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadKeyValueDriver(connection),
        BenchmarkDatabases.Redis => new RedisKeyValueDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.KeyValue + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
