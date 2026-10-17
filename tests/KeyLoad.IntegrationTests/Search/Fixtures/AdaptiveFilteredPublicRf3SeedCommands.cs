using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3SeedCommands
{
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    internal static async Task<AdaptiveFilteredPublicRf3Batch[]> RunAsync(RequestCqrsRf3Callers administrator,
        NativeAnnMaintenanceRf3Scenario scenario, CancellationToken token)
    {
        var batches = new List<AdaptiveFilteredPublicRf3Batch>();
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, scenario.Partition), token));
        for (var offset = 0; offset < AdaptiveFilteredPublicRf3Protocol.Count; offset += AdaptiveFilteredPublicRf3Protocol.BatchDocuments)
        {
            var mutations = new List<Mutation>();
            foreach (var ordinal in Enumerable.Range(offset, AdaptiveFilteredPublicRf3Protocol.BatchDocuments))
            {
                var id = AdaptiveFilteredPublicRf3Protocol.Id(ordinal);
                mutations.Add(new PutDocument(NativeAnnMaintenanceRf3Scenario.Collection, id, AdaptiveFilteredPublicRf3Protocol.Body));
                mutations.Add(new PutVector(NativeAnnMaintenanceRf3Scenario.Collection, id,
                    NativeAnnMaintenanceRf3Scenario.Field, [(float)ordinal, 1], NativeAnnMaintenanceRf3Scenario.Space, 1));
            }
            var original = new CommandRequest(Guid.NewGuid(), scenario.Partition, [.. mutations]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token));
            await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
            var expected = new CommitReceipt(original.CommandId,
                new(placement.Incarnation, scenario.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
                [.. mutations.Select((mutation, index) => new MutationReceipt(index % 2 == 0 ? PutDocumentKind : PutVectorKind,
                    NativeAnnMaintenanceRf3Scenario.Collection, AdaptiveFilteredPublicRf3Protocol.Id(offset + index / 2), 1))],
                DurabilityProfile.QuorumProcessDurable);
            await AdaptiveFilteredPublicRf3Equal.RequireAsync(expected, receipt);
            batches.Add(new(original, receipt));
        }
        return batches.ToArray();
    }
}
