using KeyLoad.Clustering;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3PlanFields
{
    private const int Version = 1;
    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        ReplicaSiloDiscovery[] discovery, TenantPlacementPlanResultV1 actual)
    {
        var plan = actual.Plan;
        await Assert.That(plan.Version).IsEqualTo(Version);
        await Assert.That(plan.PlanId).IsNotEqualTo(Guid.Empty);
        await Assert.That(plan.TenantId).IsEqualTo(state.PlanRequest.TenantId);
        await Assert.That(plan.DatabaseId).IsEqualTo(state.PlanRequest.DatabaseId);
        await Assert.That(plan.ControlPartition).IsEqualTo(state.Capacity.Control);
        await Assert.That(plan.PolicyRevision).IsEqualTo(state.Policy.Revision);
        await Assert.That(plan.DirectoryRevision).IsEqualTo(state.DirectoryRevision);
        await Assert.That(plan.OriginalPartitions.SequenceEqual(state.PlanRequest.Partitions)).IsTrue();
        await Assert.That(plan.Moves.IsEmpty).IsTrue();
        await Assert.That(plan.CapacityDigests.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(plan.CapacityDigests.All(value => !string.IsNullOrWhiteSpace(value))).IsTrue();
        await Assert.That(plan.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(discovery.Select(value => value.SiloAddress)));
        await Assert.That(plan.SampledAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(plan.ExpiresAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(plan.ExpiresAt).IsGreaterThan(plan.SampledAt);
        await Assert.That(plan.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await Assert.That(string.IsNullOrWhiteSpace(actual.SignedPlan)).IsFalse();
    }
}
