using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Movement
{
    private const int ProtocolVersion = 1;
    private const long Increment = 1;
    private const long NoPosition = 0;

    internal static Task<bool> RunAsync(TwoRf3MembershipWave wave, ChunkMinimumRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Administrator, source => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                TwoRf3MembershipProtocol.Node4, state.Administrator, async target =>
                {
                    var destination = state.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                        == state.Move.DestinationPhysicalShardId).Owner;
                    await Assert.That(destination.Incarnation).IsNotEqualTo(state.OriginalOwner.Incarnation);
                    var moved = await McpCallerAssertions.SdkSuccessAsync(await source.Sdk.MovePartitionAsync(state.Move, token));
                    var tokenValue = moved.InstalledReceipt ?? throw new InvalidOperationException("The genuine transfer has no installed token.");
                    await Assert.That(tokenValue.Position).IsGreaterThan(NoPosition);
                    await Assert.That(moved.SourceCut).IsGreaterThan(NoPosition);
                    var epoch = checked(state.OriginalOwner.PlacementEpoch + Increment);
                    var placement = new AtomicPartitionPlacementV1(ProtocolVersion, state.Partition,
                        destination.PhysicalShardId, checked(state.OriginalOwner.Revision + Increment),
                        destination.Incarnation, destination.VoterIds, epoch);
                    await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(state.Move.MoveId, state.Partition,
                        PartitionMovePhase.Retired, state.Directory.ControlOwner, destination, moved.SourceCut,
                        new CommitToken(destination.Incarnation, state.Partition.AtomicPartitionId, tokenValue.Position, epoch), placement), moved);
                    var owner = await McpCallerAssertions.SdkSuccessAsync(
                        await target.Sdk.ReadAtomicPartitionPlacementAsync(new(ProtocolVersion, state.Partition), token));
                    await Assert.That(owner.PhysicalShardId).IsEqualTo(destination.PhysicalShardId);
                    await Assert.That(owner.Incarnation).IsEqualTo(destination.Incarnation);
                    await Assert.That(owner.PlacementEpoch).IsEqualTo(epoch);
                    await Assert.That(owner.Revision).IsEqualTo(placement.Revision);
                    state.Owner = owner;
                    await ChunkMinimumRf3Pages.ConserveAsync(target, state, token);
                    var row = new SampleRecord(ChunkMinimumRf3Seed.TargetSeries,
                        TimeSeriesRf3Scenario.Data(ChunkMinimumRf3Seed.TargetId,
                            TimeSeriesRf3Scenario.Start.AddTicks(ChunkMinimumRf3Seed.TargetTicks), ChunkMinimumRf3Seed.TargetValue),
                        Increment, TimeSeriesRf3Scenario.PrivateTags);
                    var healthy = await ChunkMinimumRf3Writes.WindowAsync(target, state.Partition, state.Owner,
                        ChunkMinimumRf3Seed.TargetSeries, [row], TwoRf3MembershipProtocol.Node4, state.Originals, token);
                    state.Windows.Add(healthy);
                    state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await target.Sdk.OutboxStatusAsync(state.Partition, token));
                    await ChunkMinimumRf3Pages.ConserveAsync(target, state, token);
                    return true;
                }, token), token);
}
