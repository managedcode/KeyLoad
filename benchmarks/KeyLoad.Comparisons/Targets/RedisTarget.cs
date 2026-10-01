using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

public sealed class RedisTarget(string connectionString, string runId, string image) : IComparisonTarget
{
    private readonly string prefix = "keyload-benchmark:" + runId + ":";
    private ConnectionMultiplexer? connection;
    public TargetProfile Profile { get; private set; } = new("Redis", "unverified", "single server, no replicas",
        "AOF appendfsync=always; single-node ACK", "primary key reads", "RESP/TCP multiplexed", "Aspire password; no row/field policy", image);
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite;
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        var settings = ConfigurationOptions.Parse(connectionString);
        Profile = Profile with { Transport = settings.Ssl ? "RESP/TLS multiplexed" : "RESP/TCP multiplexed" };
        connection = await ConnectionMultiplexer.ConnectAsync(settings).WaitAsync(cancellationToken);
        var database = connection.GetDatabase();
        var config = (RedisResult[])(await database.ExecuteAsync("CONFIG", "GET", "appendonly", "appendfsync").WaitAsync(cancellationToken))!;
        var values = Enumerable.Range(0, config.Length / 2).ToDictionary(i => config[2 * i].ToString(), i => config[2 * i + 1].ToString());
        if (values.GetValueOrDefault("appendonly") != "yes" || values.GetValueOrDefault("appendfsync") != "always")
            throw new ComparisonFailure("RedisAofAlwaysRequired");
        var info = (await database.ExecuteAsync("INFO", "server").WaitAsync(cancellationToken)).ToString();
        Profile = Profile with { Version = info.Split('\n').First(line => line.StartsWith("redis_version:", StringComparison.Ordinal)).Trim() };
        foreach (var document in dataset.Documents)
        {
            RedisKey key = prefix + document.Id;
            await database.StringSetAsync(key, document.Json).WaitAsync(cancellationToken);
        }
    }
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new Session(connection!.GetDatabase(), prefix));
    public async ValueTask DisposeAsync()
    {
        // Aspire owns and removes the isolated container, including any measured keys.
        if (connection is not null) { await connection.CloseAsync(); connection.Dispose(); }
    }
    private sealed class Session(IDatabase database, string prefix) : IComparisonSession
    {
        public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        {
            var json = await database.StringGetAsync(prefix + document.Id).WaitAsync(cancellationToken);
            return json.IsNull ? null : new(document.Id, json.ToString());
        }
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (scenario == Scenario.PointRead) return new(Document: await ReadAsync(document, cancellationToken));
            if (scenario != Scenario.DocumentWrite) throw new NotSupportedException();
            if (!await database.StringSetAsync(prefix + document.Id, document.Json, when: When.NotExists).WaitAsync(cancellationToken))
                throw new ComparisonFailure("RedisCreateConflict");
            return new();
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
