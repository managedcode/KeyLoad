using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnPublicColdAssertions
{
    private const int Mutations = 6;
    private const int Documents = 3;
    private const long Revision = 1;

    internal static async Task StateAsync(RequestCqrsRf3Callers caller, NativeAnnPublicColdSeed seed,
        CancellationToken token)
    {
        await Assert.That(seed.Receipt.CommandId).IsEqualTo(seed.Command.CommandId);
        await Assert.That(seed.Receipt.Token.AtomicPartitionId).IsEqualTo(seed.Scenario.Partition.AtomicPartitionId);
        await Assert.That(seed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(seed.Receipt.Mutations.Length).IsEqualTo(Mutations);
        for (var index = 0; index < Mutations; index++)
        {
            var expected = new MutationReceipt(index < Documents ? "putDocument" : "putVector",
                NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[index % Documents], Revision);
            await ExactAsync(expected, seed.Receipt.Mutations[index]);
        }
        var replay = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed.Command, token));
        var official = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, seed.Command, token))).Value;
        await ExactAsync(seed.Receipt, replay);
        await ExactAsync(seed.Receipt, official);
        foreach (var id in NativeAnnMaintenanceRf3Scenario.Ids)
        {
            var reference = new EntityRef(seed.Scenario.Partition, NativeAnnMaintenanceRf3Scenario.Collection, id);
            var direct = await PublicReadAssertions.DocumentAsync(await caller.Sdk.GetAsync(reference, token));
            var remote = (await PublicReadAssertions.DocumentAsync(await caller.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value;
            var expected = new DocumentResult(reference, Revision, "{}", false, []);
            await ExactAsync(expected, direct);
            await ExactAsync(expected, remote);
        }
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(caller.Sdk, caller.Mcp, seed.Scenario, token);
    }

    internal static async Task RefuseWrongOwnerAsync(RequestCqrsRf3Callers caller, NativeAnnPublicColdSeed seed,
        AnnMaintenanceRequest original, CancellationToken token)
    {
        var wrong = original with
        {
            CommandId = Guid.NewGuid(),
            NodeId = Guid.NewGuid()
        };
        var denied = await global::KeyLoad.Client.AnnMaintenanceClient.MaintainAnnIndexAsync(caller.Sdk, wrong, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(
            NativeAnnMaintenanceRf3Scenario.Tool, wrong, token), ErrorCode.OwnershipLost, dispatched: true);
        await StateAsync(caller, seed, token);
    }

    internal static async Task RestoreAsync(RequestCqrsRf3Callers caller, NativeAnnPublicColdSeed seed,
        AnnMaintenanceRequest restore, CancellationToken token)
    {
        await Assert.That(restore.Consumer).IsEqualTo(seed.Pin.Consumer);
        await Assert.That(restore.IndexGeneration).IsEqualTo(seed.Pin.IndexGeneration);
        await Assert.That(restore.NodeId).IsEqualTo(seed.Pin.NodeId);
        await Assert.That(restore.Placement.Incarnation).IsEqualTo(seed.Pin.Placement.Incarnation);
        await ExactAsync(seed.Pin.Placement, restore.Placement);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.AnnMaintenanceClient.MaintainAnnIndexAsync(caller.Sdk, restore, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(sdk, restore);
        var again = restore with
        {
            CommandId = Guid.NewGuid()
        };
        var official = (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await caller.Mcp.CallAsync(
            NativeAnnMaintenanceRf3Scenario.Tool, again, token))).Value;
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(official, again);
        foreach (var result in new[] { sdk, official })
        {
            await Assert.That(result.IndexSha256).IsEqualTo(seed.Published.IndexSha256);
            await Assert.That(result.Source!.CorpusSha256).IsEqualTo(seed.Published.Source!.CorpusSha256);
            await Assert.That(result.Source.ThroughSequence).IsEqualTo(seed.Published.Source.ThroughSequence);
            await Assert.That(result.Source.SchemaVersion).IsEqualTo(seed.Published.Source.SchemaVersion);
            await Assert.That(result.Source.PolicyEpoch).IsEqualTo(seed.Published.Source.PolicyEpoch);
            await Assert.That(result.Source.NodeId).IsEqualTo(restore.NodeId);
            await Assert.That(result.Source.Incarnation).IsEqualTo(restore.Placement.Incarnation);
            await Assert.That(result.Checkpoint).IsNotNull();
            await ExactAsync(seed.Published.Checkpoint!, result.Checkpoint!);
        }
    }

    private static async Task ExactAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
