using KeyLoad.Client;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionCold
{
    private const int RequiredColdCuts = 2;
    internal static async Task RunAsync(ControlledPartitionMovementNativeHosts hosts,
        AuthenticationExecutionState state, AuthenticationExecutionCollector collector, CancellationToken token)
    {
        for (var cut = AuthenticationExecutionProtocol.NoEvents; cut < RequiredColdCuts; cut++)
        {
            var originalOwners = hosts.Nodes.ToArray();
            var identities = originalOwners.Select(owner => NativeSerialization.Serialize(owner.Store.Identity)).ToArray();
            await hosts.RestartAsync(token);
            await Assert.That(originalOwners.All(owner => owner.IsJoined)).IsTrue();
            await Assert.That(hosts.Nodes.Count).IsEqualTo(originalOwners.Length);
            for (var index = AuthenticationExecutionProtocol.NoEvents; index < identities.Length; index++)
            { await Assert.That(NativeSerialization.Serialize(hosts.Nodes[index].Store.Identity).AsSpan().SequenceEqual(identities[index])).IsTrue(); }
            using var rootHttp = new HttpClient { BaseAddress = new(hosts.Source.Runtime.Node.Value.PublicEndpoint), Timeout = Timeout.InfiniteTimeSpan };
            var root = new KeyLoadClient(rootHttp, hosts.Source.Runtime.Node.Value.AdminKey, IntegrationClientOptions.Execution());
            await AuthenticationExecutionValues.ReplayAsync(root, state, token);
            await AuthenticationExecutionClients.RunAsync(hosts.Source, state, collector, (reader, mcp, http) =>
                AuthenticationExecutionValues.HealthyAsync(reader, mcp, http, collector, state, state.Expected, token), token);
        }
    }
}
