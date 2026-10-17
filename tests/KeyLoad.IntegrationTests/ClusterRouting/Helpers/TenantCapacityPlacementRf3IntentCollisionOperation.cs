namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionOperation
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, CancellationToken token)
    {
        var firstPlan = await TenantCapacityPlacementRf3PolicyRevisionSetup.RequireAsync(wave, seed, capacity, token);
        var firstId = Guid.NewGuid();
        var request = TenantCapacityPlacementRf3IntentCollisionPersistence.Request(Guid.NewGuid(), firstPlan, capacity);
        var pending = await TenantCapacityPlacementRf3IntentCollisionPersistence.PersistAsync(seed, firstId, request, firstPlan, token);
        var first = new TenantCapacityPlacementRf3IntentCollisionState(firstId, request, pending,
            TenantCapacityPlacementRf3IntentCollisionPersistence.Move(firstPlan), Guid.Empty, request,
            TenantCapacityPlacementRf3IntentCollisionPersistence.Move(firstPlan));
        var rows = await TenantCapacityPlacementRf3IntentCollisionCold.RequireAsync(wave, seed, capacity, first, null, token);
        var secondPlan = await TenantCapacityPlacementRf3IntentCollisionPersistence.PlanAsync(seed, capacity, token);
        var collision = first with
        {
            CollisionId = Guid.NewGuid(),
            CollisionRequest = TenantCapacityPlacementRf3IntentCollisionPersistence.Request(request.IntentId, secondPlan, capacity),
            CollisionMove = TenantCapacityPlacementRf3IntentCollisionPersistence.Move(secondPlan)
        };
        await Assert.That(collision.CollisionMove.MoveId).IsNotEqualTo(first.Move.MoveId);
        await Assert.That(secondPlan.Plan.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await TenantCapacityPlacementRf3IntentCollisionAssertions.RefuseAsync(seed, collision, token);
        rows = await TenantCapacityPlacementRf3IntentCollisionCold.RequireAsync(wave, seed, capacity, collision, rows, token);
        await TenantCapacityPlacementRf3IntentCollisionRepair.RequireAsync(wave, seed, capacity, collision, rows, token);
    }
}
