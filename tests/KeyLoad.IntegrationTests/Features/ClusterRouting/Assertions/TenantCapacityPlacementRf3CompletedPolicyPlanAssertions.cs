using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3CompletedPolicyPlanAssertions
{
    private const int Version = 1;
    private const long PublicationDirectoryRevision = 2;

    internal static async Task RequireAsync(TenantPlacementPlanRequest request, TenantPlacementPolicyV1 policy,
        ReplicaSiloDiscovery[] discovery, TenantPlacementPlanResultV1 actual)
    {
        var plan = actual.Plan;
        await Assert.That(plan.Version).IsEqualTo(Version);
        await Assert.That(plan.PlanId).IsNotEqualTo(Guid.Empty);
        await Assert.That(plan.TenantId).IsEqualTo(request.TenantId);
        await Assert.That(plan.DatabaseId).IsEqualTo(request.DatabaseId);
        await Assert.That(plan.ControlPartition).IsEqualTo(request.ControlPartition);
        await Assert.That(plan.PolicyRevision).IsEqualTo(policy.Revision);
        await Assert.That(plan.DirectoryRevision).IsEqualTo(PublicationDirectoryRevision);
        await Assert.That(plan.OriginalPartitions.SequenceEqual(request.Partitions)).IsTrue();
        await Assert.That(plan.Moves.IsEmpty).IsTrue();
        await Assert.That(plan.CapacityDigests.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(plan.CapacityDigests.All(value => !string.IsNullOrWhiteSpace(value))).IsTrue();
        await Assert.That(plan.CohortFingerprint).IsEqualTo(
            TwoRf3MembershipFingerprintAssertions.Fingerprint(discovery.Select(value => value.SiloAddress)));
        await Assert.That(plan.ExpiresAt).IsGreaterThan(plan.SampledAt);
        await Assert.That(plan.ExpiresAt).IsGreaterThan(TimeProvider.System.GetUtcNow());
        await Assert.That(string.IsNullOrWhiteSpace(actual.SignedPlan)).IsFalse();
    }
}
