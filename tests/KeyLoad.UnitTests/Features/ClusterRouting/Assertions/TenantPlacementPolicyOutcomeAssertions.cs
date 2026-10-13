using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class TenantPlacementPolicyOutcomeAssertions
{
    internal static byte[] Stored(PhysicalShardCatalogFixture fixture, Guid id)
        => fixture.Store.Read(view =>
        {
            var selected = CommandOutcomeKeyResolver.Select(view, PhysicalShardCatalogFixture.RootPrincipalId, id,
                new(CommandOutcomeScopeKind.Partition, TenantPlacementPolicyOutcomeFlow.ControlPartition));
            return view.ReadOwnedValue(selected.Key)
                ?? throw new InvalidOperationException("The original scoped placement policy outcome is absent.");
        });

    internal static async Task RefusalAsync(PhysicalShardCatalogFixture fixture, ReplicatedOperation original, byte[] bytes)
    {
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo("The tenant placement authority revision changed.");
        await Assert.That(Stored(fixture, original.Id).SequenceEqual(bytes)).IsTrue();
    }

    internal static async Task UnchangedAsync(PhysicalShardCatalogFixture fixture, string[] image,
        long position, TenantPlacementPolicyV1 policy)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), fixture.Database.EvaluationClock, CancellationToken.None);
        var actual = fixture.Database.ReadPlacementPolicy(PhysicalShardCatalogFixture.RootPrincipalId,
            new(policy.TenantId, policy.DatabaseId, TenantPlacementPolicyOutcomeFlow.ControlPartition), work);
        await Assert.That(JsonData.Fingerprint(actual)).IsEqualTo(JsonData.Fingerprint(policy));
    }
}
