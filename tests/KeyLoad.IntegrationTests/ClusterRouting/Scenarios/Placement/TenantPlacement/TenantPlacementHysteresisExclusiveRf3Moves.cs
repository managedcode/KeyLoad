using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Moves
{
    private const long OccupiedDirectory = 7;
    private const long MinimumPosition = 0;
    private const int Version = 1;
    private const long ForwardSourceEpoch = 3;
    private const long ReverseSourceEpoch = 4;
    private const long ForwardPublishedEpoch = 4;
    private const long ReversePublishedEpoch = 5;

    internal static Task<PartitionMoveResult> RunAsync(TenantPlacementHysteresisExclusiveRf3State state,
        PartitionMoveRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(state.Wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Wave.Profile.AdminKey, async root =>
            {
                var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.MovePartitionAsync(request, token));
                await FieldsAsync(state, request, result);
                await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
                await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(
                    await root.Mcp.CallAsync(PartitionMovePublicProtocol.ToolName, request, token))).Value);
                await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
                return result;
            }, token);

    internal static async Task ReplayAsync(TenantPlacementHysteresisExclusiveRf3State state,
        PartitionMoveRequest request, PartitionMoveResult original, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Wave.Profile.AdminKey, async root =>
            {
                await SqlRf3Protocol.EqualAsync(original,
                    await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.MovePartitionAsync(request, token)));
                await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
                await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(
                    await root.Mcp.CallAsync(PartitionMovePublicProtocol.ToolName, request, token))).Value);
                await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
                return original;
            }, token);
    }

    private static async Task FieldsAsync(TenantPlacementHysteresisExclusiveRf3State state,
        PartitionMoveRequest request, PartitionMoveResult result)
    {
        var forward = state.DirectoryRevision == OccupiedDirectory;
        var source = (forward ? state.Seed.Directory.ControlOwner : state.Target) with
        {
            PlacementEpoch = forward ? ForwardSourceEpoch : ReverseSourceEpoch
        };
        var target = forward ? state.Target : state.Seed.Directory.ControlOwner;
        var published = new AtomicPartitionPlacementV1(Version, request.Partition, target.PhysicalShardId,
            state.DirectoryRevision, target.Incarnation, target.VoterIds,
            forward ? ForwardPublishedEpoch : ReversePublishedEpoch);
        await Assert.That(result.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(result.Partition).IsEqualTo(state.Packed.Sibling.Partition);
        await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await SqlRf3Protocol.EqualAsync(source, result.SourceOwner);
        await SqlRf3Protocol.EqualAsync(target, result.DestinationOwner);
        await SqlRf3Protocol.EqualAsync(published, result.PublishedPlacement!);
        await Assert.That(result.SourceCut).IsGreaterThan(MinimumPosition);
        await Assert.That(result.InstalledReceipt).IsNotNull();
        await Assert.That(result.InstalledReceipt!.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(result.InstalledReceipt.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(result.InstalledReceipt.OwnershipEpoch).IsEqualTo(target.PlacementEpoch);
        await Assert.That(result.InstalledReceipt.Position).IsGreaterThan(MinimumPosition);
    }
}
