using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Current
{
    private const int Version = 1;
    private const long OriginalDirectory = 6;
    private const long OccupiedDirectory = 7;
    private const long RepairedDirectory = 8;
    private const long PrimaryRow = 6;
    private const long PrimaryEpoch = 2;
    private const long ControlRow = 1;
    private const long ForeignOriginalRow = 5;
    private const long ForeignOriginalEpoch = 3;
    private const long ForeignOccupiedEpoch = 4;
    private const long ForeignRepairedEpoch = 5;

    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        CancellationToken token)
    {
        var scope = new TenantPlacementScope(state.Policy.TenantId, state.Policy.DatabaseId, state.Capacity.Control);
        await SqlRf3Protocol.EqualAsync(state.Policy, await McpCallerAssertions.SdkSuccessAsync(
            await state.Seed.Source.ReadPlacementPolicyAsync(scope, token)));
        await SqlRf3Protocol.EqualAsync(state.Policy, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await state.Seed.Official.CallAsync(TenantPlacementPublicProtocol.PolicyTool,
                new
                {
                    request = scope
                }, token))).Value);
        await RowAsync(state, state.Seed.Partition, state.Target, PrimaryRow, PrimaryEpoch, token);
        await RowAsync(state, state.Capacity.Control, state.Seed.Directory.ControlOwner,
            ControlRow, state.Seed.Directory.ControlOwner.PlacementEpoch, token);
        var occupied = state.DirectoryRevision == OccupiedDirectory;
        var row = state.DirectoryRevision == OriginalDirectory ? ForeignOriginalRow : state.DirectoryRevision;
        var epoch = state.DirectoryRevision == OriginalDirectory ? ForeignOriginalEpoch
            : occupied ? ForeignOccupiedEpoch : ForeignRepairedEpoch;
        await Assert.That(state.DirectoryRevision is OriginalDirectory or OccupiedDirectory or RepairedDirectory).IsTrue();
        await RowAsync(state, state.Packed.Sibling.Partition,
            occupied ? state.Target : state.Seed.Directory.ControlOwner, row, epoch, token);
        await TenantPlacementHysteresisExclusiveRf3Documents.RequireAsync(state, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(state.Seed, state.Capacity, token);
    }

    private static async Task RowAsync(TenantPlacementHysteresisExclusiveRf3State state,
        PartitionRef partition, PhysicalShardRecord owner, long row, long epoch, CancellationToken token)
    {
        var expected = new AtomicPartitionPlacementResolution(Version, partition, owner.PhysicalShardId,
            owner.Incarnation, owner.VoterIds, epoch, state.DirectoryRevision, row, false);
        var request = new AtomicPartitionPlacementReadRequest(Version, partition);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await state.Seed.Source.ReadAtomicPartitionPlacementAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await state.Seed.Official.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
    }
}
