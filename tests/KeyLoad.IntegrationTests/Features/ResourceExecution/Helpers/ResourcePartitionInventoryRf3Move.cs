using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Move
{
    private const int FirstOriginal = 0;
    private const int PlacementVersion = 1;
    private const string BeforeFirst = "move-before-a";
    private const string BeforeSecond = "move-before-b";
    private const string AfterFirst = "move-after-a";
    private const string AfterSecond = "move-after-b";
    private const string Json = "{\"private\":\"d2-inventory-private-payload\",\"value\":51}";

    internal static Task<ResourcePartitionInventoryRf3MoveState> RunAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, ResourcePartitionInventoryRf3Baseline baseline,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                var partition = seed.Originals[FirstOriginal].Command.Partition;
                var before = await PairAsync(callers, partition, beforeMove: true, token);
                await ResourcePartitionInventoryRf3PublicAssertions.ReplayAsync(wave,
                    seed with
                    {
                        Creations = [],
                        Originals = before
                    }, token);
                var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
                var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk
                    .ReadAtomicPartitionPlacementAsync(new(PlacementVersion, partition), token));
                var target = directory.Owners.Single(owner => owner.Owner.PhysicalShardId != placement.PhysicalShardId);
                var request = new PartitionMoveRequest(Guid.NewGuid(), partition, target.Owner.PhysicalShardId,
                    placement.Revision, PartitionMoveMode.Transfer);
                var terminal = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.MovePartitionAsync(request, token));
                await ResourcePartitionInventoryRf3MoveTerminal.VerifyAsync(callers, directory, placement, request, terminal, token);
                foreach (var original in before)
                {
                    await Assert.That(terminal.SourceCut).IsGreaterThanOrEqualTo(original.Receipt.Token.Position);
                }
                var after = await PairAsync(callers, partition, beforeMove: false, token);
                var originals = seed with
                {
                    Creations = [],
                    Originals = before.AddRange(after)
                };
                using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
                var inventory = await ResourcePartitionInventoryRf3MoveInventory.CaptureAsync(
                    wave, seed, membership, baseline, terminal, token);
                var state = new ResourcePartitionInventoryRf3MoveState(request, terminal, originals, baseline, inventory);
                await ResourcePartitionInventoryRf3MoveReplay.VerifyAsync(wave, seed, state, token);
                return state;
            }, token);

    private static async Task<ImmutableArray<ResourcePartitionInventoryRf3Original>> PairAsync(
        RequestCqrsRf3Callers callers, PartitionRef partition, bool beforeMove, CancellationToken token)
        => [await ResourcePartitionInventoryRf3Corpus.WriteAsync(callers, partition,
                ResourcePartitionInventoryRf3Protocol.First, beforeMove ? BeforeFirst : AfterFirst,
                Json, official: false, token),
            await ResourcePartitionInventoryRf3Corpus.WriteAsync(callers, partition,
                ResourcePartitionInventoryRf3Protocol.Second, beforeMove ? BeforeSecond : AfterSecond,
                Json, official: true, token)];
}
