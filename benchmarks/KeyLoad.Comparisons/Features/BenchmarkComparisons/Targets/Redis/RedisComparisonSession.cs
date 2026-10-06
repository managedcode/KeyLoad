using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal sealed class RedisComparisonSession(ConnectionMultiplexer connection, string prefix, ComparisonTopology topology, int corpusCount,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions)
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
    private readonly IDatabase database = connection.GetDatabase();

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string ScaledCorpusReadbackMissingRecordDetail = "ScaledCorpusReadbackMissingRecord";

        const int NoObservedItems = 0;
        const string DToken = "d?????????";
        const string ScaledCorpusReadbackExtraRecordDetail = "ScaledCorpusReadbackExtraRecord";
        const string ScaledCorpusReadbackCountMismatchDetail = "ScaledCorpusReadbackCountMismatch";
        const int FirstElementIndex = 0;

        const int pageSize = 256;
        var endpoint = connection.GetEndPoints(configuredOnly: true).Single();
        var server = connection.GetServer(endpoint);
        var observed = NoObservedItems;
        await foreach (var _ in server.KeysAsync(pattern: prefix + DToken, pageSize: pageSize).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            observed++;
            if (observed > corpusCount)
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackExtraRecordDetail);
            }
        }
        if (observed != corpusCount)
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackCountMismatchDetail);
        }
        for (var offset = FirstElementIndex; offset < corpusCount; offset += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(pageSize, corpusCount - offset);
            var keys = new RedisKey[count];
            for (var index = FirstElementIndex; index < count; index++)
            {
                keys[index] = prefix + ScaledComparisonCorpus.Id(offset + index);
            }
            var values = await database.StringGetAsync(keys, CommandFlags.DemandMaster).WaitAsync(cancellationToken);
            for (var index = FirstElementIndex; index < values.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (values[index].IsNull)
                {
                    throw new ComparisonFailureException(ScaledCorpusReadbackMissingRecordDetail);
                }
                yield return new(ScaledComparisonCorpus.Id(offset + index), values[index].ToString());
            }
        }
    }

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var json = await database.StringGetAsync(prefix + document.Id, CommandFlags.DemandMaster).WaitAsync(cancellationToken);
        return json.IsNull ? null : new(document.Id, json.ToString());
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, token));
        }
        if (scenario is not (Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete))
        {
            throw new NotSupportedException();
        }
        var replicated = ComparisonTopologies.NodeCount(topology) > SingleItemCount;
        var before = replicated ? await ReadClientIdAsync(token) : NoObservedItems;
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
        const int FsyncReceiptFieldCount = 2;
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        if (await ReadClientIdAsync(token) != before)
        {
            throw new ComparisonFailureException(WriteConnectionReplaced);
        }
        var timeoutMilliseconds = checked((int)Math.Ceiling(lifecycleOptions.Value.RedisReceiptTimeout.TotalMilliseconds));
        var arguments = new object[] { RequiredLocalFsync, RequiredReplicaFsync, timeoutMilliseconds };
        var reply = (RedisResult[])(await database.ExecuteAsync(WaitAofCommand, arguments, CommandFlags.DemandMaster).WaitAsync(token))!;
        if (reply.Length != FsyncReceiptFieldCount || (long)reply[FirstElementIndex] < RequiredLocalFsync || (long)reply[SingleItemCount] < RequiredReplicaFsync)
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
