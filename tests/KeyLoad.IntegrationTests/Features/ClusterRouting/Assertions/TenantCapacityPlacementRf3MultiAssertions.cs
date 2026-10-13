using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiAssertions
{
    private const int PlanVersion = 1;
    private const int PartitionCount = 2;
    private const long ControlRevision = 1;
    private const long FallbackRevision = 0;
    private const long FirstPublicationRevision = 2;
    private const int CohortSize = 6;
    internal static async Task<(Guid Id, BindAtomicPartitionPlacementRequest Request)> BindControlAsync(
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new BindAtomicPartitionPlacementRequest(PlanVersion, FallbackRevision, state.Control,
            seed.Directory.ControlOwner.PhysicalShardId);
        await ReplayBindingAsync(seed, id, request, token);
        var control = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadAtomicPartitionPlacementAsync(new(PlanVersion, state.Control), token));
        await Assert.That(control.IsFallback).IsFalse();
        await Assert.That(control.Revision).IsEqualTo(ControlRevision);
        await Assert.That(control.DirectoryRevision).IsEqualTo(ControlRevision);
        await Assert.That(control.PhysicalShardId).IsEqualTo(request.PhysicalShardId);
        return (id, request);
    }

    internal static async Task ReplayBindingAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, BindAtomicPartitionPlacementRequest request, CancellationToken token)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BindAtomicPartitionPlacementAsync(id, request, token))).IsTrue();
        await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(
            await seed.Official.CallAsync(McpCallerTools.AdminPartitionPlacementBind,
                new { commandId = id, request }, token))).Value).IsTrue();
    }

    internal static async Task PlanAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 plan)
    {
        await Assert.That(plan.Plan.Version).IsEqualTo(PlanVersion);
        await Assert.That(plan.Plan.ControlPartition).IsEqualTo(state.Control);
        await Assert.That(plan.Plan.DirectoryRevision).IsEqualTo(ControlRevision);
        await Assert.That(plan.Plan.Moves.Length).IsEqualTo(PartitionCount);
        await Assert.That(plan.Plan.Moves.Select(move => move.MoveId).Distinct().Count()).IsEqualTo(PartitionCount);
        await Assert.That(plan.Plan.CapacityDigests.Length).IsEqualTo(CohortSize);
        await Assert.That(plan.Plan.ExpiresAt).IsGreaterThan(plan.Plan.SampledAt);
        foreach (var move in plan.Plan.Moves)
        {
            var seed = seeds.Single(value => value.Partition == move.Partition);
            await Assert.That(seed.OriginalPlacement.IsFallback).IsTrue();
            await Assert.That(seed.OriginalPlacement.Revision).IsEqualTo(FallbackRevision);
            await Assert.That(move.MoveId).IsNotEqualTo(Guid.Empty);
            await Assert.That(move.ExpectedPlacementRevision).IsEqualTo(FallbackRevision);
            await Assert.That(move.DestinationPhysicalShardId).IsEqualTo(seed.FirstRequest.DestinationPhysicalShardId);
            await Assert.That(move.Mode).IsEqualTo(PartitionMoveMode.Transfer);
        }
    }

    internal static async Task PlacementsAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, PartitionMoveRequest[] moves,
        int completed, CancellationToken token)
    {
        var directory = ControlRevision + completed;
        for (var index = 0; index < moves.Length; index++)
        {
            var move = moves[index];
            var row = await McpCallerAssertions.SdkSuccessAsync(
                await seed.Source.ReadAtomicPartitionPlacementAsync(new(PlanVersion, move.Partition), token));
            await Assert.That(row.DirectoryRevision).IsEqualTo(directory);
            await Assert.That(row.Revision).IsEqualTo(index < completed ? FirstPublicationRevision + index : FallbackRevision);
            await Assert.That(row.IsFallback).IsEqualTo(index >= completed);
            await Assert.That(row.PhysicalShardId).IsEqualTo(index < completed
                ? move.DestinationPhysicalShardId : seed.Directory.ControlOwner.PhysicalShardId);
        }
        var control = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadAtomicPartitionPlacementAsync(new(PlanVersion, state.Control), token));
        await Assert.That(control.Revision).IsEqualTo(ControlRevision);
        await Assert.That(control.DirectoryRevision).IsEqualTo(directory);
        await Assert.That(control.PhysicalShardId).IsEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
    }
}
