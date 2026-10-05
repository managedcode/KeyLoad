using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsAuthorityFaultDiscovery
{
    internal static async Task<ReplicaSiloDiscovery[]> ReadAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var observations = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < observations.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
        }
        return observations;
    }
}
