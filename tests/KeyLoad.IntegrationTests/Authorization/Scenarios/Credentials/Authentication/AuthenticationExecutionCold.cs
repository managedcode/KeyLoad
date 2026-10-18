using KeyLoad.Client;
using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionCold
{
    private const int RequiredColdCuts = 2;
    internal static async Task RunAsync(RequestCqrsHostedCluster hosts,
        AuthenticationExecutionState state, AuthenticationExecutionCollector collector, CancellationToken token)
    {
        for (var cut = AuthenticationExecutionProtocol.NoEvents; cut < RequiredColdCuts; cut++)
        {
            var originalOwners = hosts.Nodes.Select(host => host.Services.GetRequiredService<PartitionHostApplicationOwner>()).ToArray();
            var originalNodes = hosts.Nodes.Select(host => host.Services.GetRequiredService<OrleansNode>()).ToArray();
            var identities = hosts.Nodes.Select(host => NativeSerialization.Serialize(host.Database.Store.Identity)).ToArray();
            await hosts.StopAsync(token);
            await Assert.That(originalOwners.All(owner => owner.Closing is { IsCompletedSuccessfully: true } && owner.CanReleaseRoot)).IsTrue();
            await Assert.That(originalNodes.All(node => node.Closing is { IsCompletedSuccessfully: true } && node.HasJoinedRequestWork)).IsTrue();
            await hosts.RestartAsync(token);
            await Assert.That(hosts.Nodes.Length).IsEqualTo(originalOwners.Length);
            for (var index = AuthenticationExecutionProtocol.NoEvents; index < identities.Length; index++)
            {
                await Assert.That(NativeSerialization.Serialize(hosts.Nodes[index].Database.Store.Identity).AsSpan().SequenceEqual(identities[index])).IsTrue();
            }
            var source = hosts.Nodes[AuthenticationExecutionProtocol.NoEvents];
            using var rootHttp = new HttpClient { BaseAddress = source.PublicEndpoint, Timeout = Timeout.InfiniteTimeSpan };
            var root = new KeyLoadClient(rootHttp, source.AdminKey, IntegrationClientOptions.Execution());
            await AuthenticationExecutionValues.ReplayAsync(root, state, token);
            await AuthenticationExecutionClients.RunAsync(source, state, collector, (reader, mcp, http) =>
                AuthenticationExecutionValues.HealthyAsync(reader, mcp, http, collector, state, state.Expected, token), token);
        }
    }
}
