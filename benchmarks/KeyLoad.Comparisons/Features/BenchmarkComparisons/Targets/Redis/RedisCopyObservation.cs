using System.Net;
using StackExchange.Redis;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisCopyObservation
{
    private const string GetCommand = "GET";
    private const string LinkDown = "down";
    private const string ErrorReplicaCopy = "RedisDirectReplicaProbeFailed";

    internal static async Task VerifyDirectCopiesAsync(ConnectionMultiplexer[] replicas, EndPoint[] endpoints,
        int database, string key, string payload, CancellationToken token,
        IOptions<ComparisonLifecycleOptions> lifecycleOptions)
    {
        var lifecycle = lifecycleOptions.Value;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(lifecycle.ReadinessTimeout);
        while (true)
        {
            if (await AllCopiesPresentAsync(replicas, endpoints, database, key, payload, deadline.Token))
            {
                return;
            }
            await DelayUntilNextProbeAsync(deadline.Token, token, lifecycle.RedisReadinessPollInterval);
        }
    }

    private static async Task<bool> AllCopiesPresentAsync(ConnectionMultiplexer[] replicas, EndPoint[] endpoints,
        int database, string key, string payload, CancellationToken token)
    {
        var allPresent = true;
        for (var index = 0; index < replicas.Length; index++)
        {
            var server = replicas[index].GetServer(endpoints[index]);
            if (!await HasUpLinkAsync(server, token))
            {
                allPresent = false;
                continue;
            }
            var reply = await server.ExecuteAsync(database, GetCommand, new object[] { key }, CommandFlags.DemandReplica)
                .WaitAsync(token);
            if (reply.ToString() != payload)
            {
                allPresent = false;
            }
        }

        return allPresent;
    }

    private static async Task<bool> HasUpLinkAsync(IServer server, CancellationToken token)
    {
        var info = await RedisNativeProtocol.ReadInfoAsync(server, RedisNativeProtocol.ReplicationSection,
            CommandFlags.DemandReplica, token);
        return info.GetValueOrDefault(RedisNativeProtocol.LinkField) switch
        {
            RedisNativeProtocol.LinkUp => true,
            LinkDown => false,
            _ => throw new ComparisonFailureException(ErrorReplicaCopy)
        };
    }

    private static async Task DelayUntilNextProbeAsync(CancellationToken deadline, CancellationToken caller, TimeSpan pollInterval)
    {
        try
        {
            await Task.Delay(pollInterval, deadline);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new ComparisonFailureException(ErrorReplicaCopy);
        }
    }
}
