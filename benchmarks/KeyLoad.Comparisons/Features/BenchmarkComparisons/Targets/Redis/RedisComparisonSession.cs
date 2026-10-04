using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal sealed class RedisComparisonSession(ConnectionMultiplexer connection, string prefix, ComparisonTopology topology)
    : IComparisonSession
{
    private const string ClientCommand = "CLIENT";
    private const string ClientIdSubcommand = "ID";
    private const string WaitAofCommand = "WAITAOF";
    private const string WriteConnectionReplaced = "RedisWriteConnectionReplaced";
    private const string ReceiptConnectionReplaced = "RedisReceiptConnectionReplaced";
    private const string WaitAofFailed = "RedisWaitAofFailed";
    private const int RequiredLocalFsync = 1;
    private const int RequiredReplicaFsync = 1;
    private const int ReceiptTimeoutMilliseconds = 3000;
    private readonly IDatabase database = connection.GetDatabase();

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var json = await database.StringGetAsync(prefix + document.Id, CommandFlags.DemandMaster).WaitAsync(cancellationToken);
        return json.IsNull ? null : new(document.Id, json.ToString());
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, token));
        }
        if (scenario is not (Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete))
        {
            throw new NotSupportedException();
        }
        var replicated = ComparisonTopologies.NodeCount(topology) > 1;
        var before = replicated ? await ReadClientIdAsync(token) : 0;
        await MutateAsync(scenario, document, token);
        if (replicated)
        {
            await RequireReplicaReceiptAsync(before, token);
        }
        return new();
    }

    private async Task MutateAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        if (scenario == Scenario.DocumentDelete)
        {
            RedisMutationContract.RequireDelete(await database.KeyDeleteAsync(prefix + document.Id,
                CommandFlags.DemandMaster).WaitAsync(token));
        }
        else
        {
            var when = scenario == Scenario.DocumentWrite ? When.NotExists : When.Exists;
            RedisMutationContract.RequireSet(await database.StringSetAsync(prefix + document.Id, document.Json,
                when: when, flags: CommandFlags.DemandMaster).WaitAsync(token), scenario);
        }
    }

    private async Task RequireReplicaReceiptAsync(long before, CancellationToken token)
    {
        if (await ReadClientIdAsync(token) != before)
        {
            throw new ComparisonFailureException(WriteConnectionReplaced);
        }
        var arguments = new object[] { RequiredLocalFsync, RequiredReplicaFsync, ReceiptTimeoutMilliseconds };
        var reply = (RedisResult[])(await database.ExecuteAsync(WaitAofCommand, arguments, CommandFlags.DemandMaster).WaitAsync(token))!;
        if (reply.Length != 2 || (long)reply[0] < RequiredLocalFsync || (long)reply[1] < RequiredReplicaFsync)
        {
            throw new ComparisonFailureException(WaitAofFailed);
        }
        if (await ReadClientIdAsync(token) != before)
        {
            throw new ComparisonFailureException(ReceiptConnectionReplaced);
        }
    }

    private async Task<long> ReadClientIdAsync(CancellationToken token)
        => (long)await database.ExecuteAsync(ClientCommand, new object[] { ClientIdSubcommand }, CommandFlags.DemandMaster).WaitAsync(token);

    public async ValueTask DisposeAsync()
    {
        await connection.CloseAsync();
        connection.Dispose();
    }
}
