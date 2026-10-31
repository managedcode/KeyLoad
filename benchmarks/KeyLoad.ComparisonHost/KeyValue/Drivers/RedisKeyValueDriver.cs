using System.Collections.Concurrent;
using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.ComparisonHost.Measurement;
using StackExchange.Redis;

namespace KeyLoad.ComparisonHost.KeyValue;

/// <summary>
/// Redis strings through StackExchange.Redis on the primary: pipelined <c>SET</c> batches, <c>GET</c> and <c>DEL</c>. The
/// primary runs <c>appendfsync always</c>; on three nodes every write batch also carries <c>WAITAOF 1 1 0</c>, so it
/// returns only after the primary and one replica fsynced it.
/// </summary>
/// <remarks>
/// <c>WAITAOF</c> blocks the connection it is sent on, so each call borrows its own multiplexer from a pool instead of
/// sharing one; a shared connection would queue every client behind another client's receipt.
/// </remarks>
internal sealed class RedisKeyValueDriver(BenchmarkConnection connection) : IKeyValueDriver
{
    private const string WaitAof = "WAITAOF";
    private const int LocalFsyncs = 1;
    private const int ReplicaFsyncs = 1;
    private const int UntilAcknowledged = 0;
    private const int Batch = 256;
    private const string MissingKey = "Redis has no key ";
    private const string NotAcknowledged = "Redis WAITAOF did not confirm one local and one replica fsync.";
    private static readonly object[] ReceiptArguments = [LocalFsyncs, ReplicaFsyncs, UntilAcknowledged];
    private readonly string connectionString = connection.RequireConnectionString();
    private readonly bool replicated = connection.Nodes > 1;
    private readonly ConcurrentBag<ConnectionMultiplexer> idle = [];
    private readonly ConcurrentQueue<ConnectionMultiplexer> opened = new();

    public int BatchSize => Batch;

    /// <summary>Opens one connection per standard client so the first measured calls do not pay for connecting.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        var connections = await Task.WhenAll(Enumerable.Range(0, BenchmarkSettings.StandardClients).Select(_ => OpenAsync()))
            .WaitAsync(token);
        foreach (var multiplexer in connections)
        {
            idle.Add(multiplexer);
        }
    }

    public Task SetManyAsync(IReadOnlyList<KeyValueRecord> records, CancellationToken token)
        => WriteAsync(batch => Task.WhenAll(records.Select(record =>
            batch.StringSetAsync(record.Key, record.Value, flags: CommandFlags.DemandMaster))), token);

    public async Task<string> GetAsync(string key, CancellationToken token)
    {
        var value = await UseAsync(database => database.StringGetAsync(key, CommandFlags.DemandMaster), token);
        return value.IsNull ? throw new KeyNotFoundException(MissingKey + key) : value.ToString();
    }

    public Task SetAsync(KeyValueRecord record, CancellationToken token)
        => WriteAsync(batch => batch.StringSetAsync(record.Key, record.Value, flags: CommandFlags.DemandMaster), token);

    public async Task DeleteAsync(string key, CancellationToken token)
    {
        if (!await WriteAsync(batch => batch.KeyDeleteAsync(key, CommandFlags.DemandMaster), token))
        {
            throw new KeyNotFoundException(MissingKey + key);
        }
    }

    public async ValueTask DisposeAsync()
    {
        while (opened.TryDequeue(out var multiplexer))
        {
            await multiplexer.DisposeAsync();
        }
    }

    /// <summary>Sends the write and, on three nodes, its fsync receipt as one pipeline on one borrowed connection.</summary>
    private Task<T> WriteAsync<T>(Func<IBatch, Task<T>> write, CancellationToken token)
        => UseAsync(async database =>
        {
            var batch = database.CreateBatch();
            var result = write(batch);
            var receipt = replicated ? RequireReceiptAsync(batch.ExecuteAsync(WaitAof, ReceiptArguments, CommandFlags.DemandMaster))
                : Task.CompletedTask;
            batch.Execute();
            await Task.WhenAll(result, receipt);
            return await result;
        }, token);

    private async Task<T> UseAsync<T>(Func<IDatabase, Task<T>> operation, CancellationToken token)
    {
        var multiplexer = await RentAsync();
        try
        {
            return await operation(multiplexer.GetDatabase()).WaitAsync(token);
        }
        finally
        {
            idle.Add(multiplexer);
        }
    }

    /// <summary>An idle pooled connection, or a new one; every connection stays owned by <c>opened</c> until disposal.</summary>
    private async Task<ConnectionMultiplexer> RentAsync()
    {
        while (idle.TryTake(out var pooled))
        {
            return pooled;
        }
        return await OpenAsync();
    }

    private async Task<ConnectionMultiplexer> OpenAsync()
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        opened.Enqueue(multiplexer);
        return multiplexer;
    }

    private static async Task RequireReceiptAsync(Task<RedisResult> receipt)
    {
        if ((long[]?)await receipt is not [>= LocalFsyncs, >= ReplicaFsyncs])
        {
            throw new InvalidOperationException(NotAcknowledged);
        }
    }
}
