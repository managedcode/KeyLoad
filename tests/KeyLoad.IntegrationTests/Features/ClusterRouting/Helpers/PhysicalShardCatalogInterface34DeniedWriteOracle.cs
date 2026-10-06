using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogInterface34DeniedWriteOracle
{
    private const string InvalidStore = "The denied interface4 mixed-cohort write exists in a stopped voter store.";

    internal static async Task VerifyAbsentAsync(string dataRoot, NodeEpochRf3Profile profile,
        EntityRef reference, CancellationToken cancellationToken)
    {
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await VerifyNodeAsync(dataRoot, profile, reference, RequestCqrsRf3Protocol.NodeName(index), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task VerifyNodeAsync(string dataRoot, NodeEpochRf3Profile profile, EntityRef reference,
        string node, CancellationToken cancellationToken)
    {
        var nodeRoot = Path.Combine(dataRoot, node);
        var options = NodeEpochRf3OfflineOptions.Create(nodeRoot, profile, node);
        await using var host = new PartitionHost(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(), new CommandAdmissionGovernor(IntegrationAdmissionOptions.Command(options.CommandAdmission)), TimeProvider.System, NullLogger<ReplicaConsensus>.Instance);
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Database.GetDocument(PartitionStoreProtocol.AdministratorId, reference) is not null)
        { throw new InvalidOperationException(InvalidStore); }
    }
}
