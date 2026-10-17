namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3CompletedPolicyReplay
{
    private const long RevisionStep = 1;

    internal sealed record Identity(PlacementIntentV1 Pending, Guid PersistId, PersistPlacementIntentRequest Persist,
        PlacementIntentV1 Completed, Guid ApplyId, ApplyPlacementIntentRequest Apply,
        PlacementIntentV1 OldIntent, Guid OldPersistId, TenantCapacityPlacementRf3RevocationCold.Rows OldRows);

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantCapacityPlacementRf3RevocationCaller caller,
        TenantCapacityPlacementRf3CompletedPolicyReplay.Identity original, CancellationToken token)
    {
        var scope = new TenantPlacementScope(seed.Partition.TenantId, seed.Partition.DatabaseId, state.Control);
        var query = new TenantPlacementPlanRequest(scope.TenantId, scope.DatabaseId, [seed.Partition], state.Control);
        var disabled = state.HealthyPolicy with
        {
            Revision = checked(state.HealthyPolicy.Revision + RevisionStep),
            Enabled = false
        };
        var id = Guid.NewGuid();
        var request = new ConfigurePlacementPolicyRequest(state.HealthyPolicy.Revision, disabled, state.Control);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ConfigureAsync(caller, id, request, disabled, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ReplayAsync(caller, original, token);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.CurrentAsync(caller, scope, disabled, original.Completed, token);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.EpochFenceAsync(caller, scope, disabled, original.OldIntent, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.DisabledPlanAsync(caller, query, token);
        await ColdAsync(wave, seed, state with
        {
            HealthyPolicy = disabled
        }, caller, original, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.DisabledPlanAsync(caller, query, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ReplayAsync(caller, original, token);
        var repaired = disabled with
        {
            Revision = checked(disabled.Revision + RevisionStep),
            Enabled = true
        };
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ConfigureAsync(caller, Guid.NewGuid(),
            new(disabled.Revision, repaired, state.Control), repaired, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.FreshPlanAsync(wave, caller, query, repaired,
            token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ConfigureAsync(caller, id, request, disabled, token);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.CurrentAsync(caller, scope, repaired, original.Completed, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ReplayAsync(caller, original, token);
        await ColdAsync(wave, seed, state with
        {
            HealthyPolicy = repaired
        }, caller, original, token);
        await TenantCapacityPlacementRf3ReadAuthorityAssertions.CurrentAsync(caller, scope, repaired, original.Completed, token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.FreshPlanAsync(wave, caller, query, repaired,
            token);
        await TenantCapacityPlacementRf3CompletedPolicyAssertions.ReplayAsync(caller, original, token);
    }

    private static Task ColdAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State current, TenantCapacityPlacementRf3RevocationCaller caller,
        TenantCapacityPlacementRf3CompletedPolicyReplay.Identity original, CancellationToken token)
        => TenantCapacityPlacementRf3RevocationHealthyCold.RequireAsync(wave, seed, current, caller,
            original.Completed, original.ApplyId, original.Apply, original.OldIntent,
            original.OldPersistId, original.OldRows, token);
}
