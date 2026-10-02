using System.Net;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisCopyObservation
{
    private const string GetCommand = "GET";
    private const string ErrorReplicaCopy = "RedisDirectReplicaProbeFailed";
    private const int PollMilliseconds = 200;
    private static readonly TimeSpan ReplicaProbeTimeout = TimeSpan.FromSeconds(60);

    internal static async Task VerifyDirectCopiesAsync(ConnectionMultiplexer[] replicas, EndPoint[] endpoints,
        int database, string key, string payload, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ReplicaProbeTimeout);
        while (true)
        {
            if (await AllCopiesPresentAsync(replicas, endpoints, database, key, payload, deadline.Token))
            {
                return;
            }
            await DelayUntilNextProbeAsync(deadline.Token, token);
        }
    }

    private static async Task<bool> AllCopiesPresentAsync(ConnectionMultiplexer[] replicas, EndPoint[] endpoints,
        int database, string key, string payload, CancellationToken token)
    {
        var allPresent = true;
        for (var index = 0; index < replicas.Length; index++)
        {
            var server = replicas[index].GetServer(endpoints[index]);
            var reply = await server.ExecuteAsync(database, GetCommand, new object[] { key }, CommandFlags.DemandReplica)
                .WaitAsync(token);
            if (reply.ToString() != payload)
            {
                allPresent = false;
            }
        }

        return allPresent;
    }

    private static async Task DelayUntilNextProbeAsync(CancellationToken deadline, CancellationToken caller)
    {
        try
        {
            await Task.Delay(PollMilliseconds, deadline);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new ComparisonFailureException(ErrorReplicaCopy);
        }
    }
}
