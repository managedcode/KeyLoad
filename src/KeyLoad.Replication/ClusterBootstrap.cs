using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Replication;

public static class ClusterBootstrap
{
    public static async Task SeedAsync(IClusterConfigurationStorage<UriEndPoint> voters, string[] peers)
    {
        var configuration = await voters.LoadConfigurationAsync().ConfigureAwait(false);
        var expected = peers.Select(peer => new UriEndPoint(new(peer.TrimEnd('/') + "/raft"))).ToArray();
        if (configuration.Members.Count != 0)
        {
            if (!expected.Select(e => e.Uri).ToHashSet().SetEquals(configuration.Members.Select(e => e.Uri)))
                throw Errors.Fail(ErrorCode.Conflict, "Persisted voters differ from the bootstrap configuration. Use an explicit membership migration.");
            return;
        }
        // This isolated container holds only the library's seed codec, not application singletons.
        await using var seed = new ServiceCollection().UseInMemoryConfigurationStorage(members =>
        { foreach (var endpoint in expected) members.Add(endpoint); }).BuildServiceProvider();
        var initial = await seed.GetRequiredService<IClusterConfigurationStorage<UriEndPoint>>().LoadConfigurationAsync().ConfigureAwait(false);
        await voters.SaveConfigurationAsync(initial, 0).ConfigureAwait(false);
    }
}
