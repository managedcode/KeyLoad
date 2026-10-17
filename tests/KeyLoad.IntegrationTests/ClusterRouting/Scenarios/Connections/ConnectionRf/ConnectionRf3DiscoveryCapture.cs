using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3DiscoveryCapture
{
    internal static async Task<ReplicaSiloDiscovery[]> ReadAsync(RequestCqrsRf3Wave wave,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var discovery = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < discovery.Length; index++)
        {
            discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.App,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
        }
        return discovery;
    }
}
