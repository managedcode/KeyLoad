using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class TenantPlacementPolicyOutcomeFlow
{
    private const string ControlPartitionName = "control";
    private static readonly Guid First = Guid.Parse("c2397ad1-e92b-40e0-8dd9-e0105b6d9a01");
    private static readonly Guid Refused = Guid.Parse("c2397ad1-e92b-40e0-8dd9-e0105b6d9a02");
    private static readonly Guid Healthy = Guid.Parse("c2397ad1-e92b-40e0-8dd9-e0105b6d9a03");
    internal static readonly PartitionRef ControlPartition = new("system", "capacity-policy", "catalog", ControlPartitionName);

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture(
                PhysicalOwnerDirectoryWholeFlow.Control.Owner.Incarnation, PhysicalOwnerDirectoryWholeFlow.Control.Owner);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogFixture fixture)
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        var original = Policy(1, 64 * 1024 * 1024, 32 * 1024 * 1024);
        var configured = fixture.Database.Apply(Operation(fixture, First, new(0, original, ControlPartition)));
        await Assert.That(configured.Error).IsNull();
        await Assert.That(JsonData.Fingerprint(configured.Get<TenantPlacementPolicyV1>())).IsEqualTo(JsonData.Fingerprint(original));
        var stale = Operation(fixture, Refused, new(0, original with
        {
            PromotionBytes = 96 * 1024 * 1024
        }, ControlPartition));
        var refusal = fixture.Database.Apply(stale);
        await Assert.That(refusal.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refusal.SafeDetail).IsEqualTo("The tenant placement authority revision changed.");
        await Assert.That(refusal.Json).IsNull();
        await Assert.That(refusal.NativeValue).IsNull();
        var image = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var position = fixture.Store.Position;
        var bytes = TenantPlacementPolicyOutcomeAssertions.Stored(fixture, Refused);
        await TenantPlacementPolicyOutcomeAssertions.UnchangedAsync(fixture, image, position, original);
        fixture.Reopen();
        await TenantPlacementPolicyOutcomeAssertions.UnchangedAsync(fixture, image, position, original);
        await TenantPlacementPolicyOutcomeAssertions.RefusalAsync(fixture, stale, bytes);
        var changed = Operation(fixture, Refused, new(1, Policy(2, 96 * 1024 * 1024, 48 * 1024 * 1024), ControlPartition));
        await Assert.That(fixture.Database.Apply(changed).Error).IsEqualTo(ErrorCode.Conflict);
        await TenantPlacementPolicyOutcomeAssertions.UnchangedAsync(fixture, image, position, original);
        var repaired = Policy(2, 96 * 1024 * 1024, 48 * 1024 * 1024);
        var healthy = Operation(fixture, Healthy, new(1, repaired, ControlPartition));
        await Assert.That(JsonData.Fingerprint(fixture.Database.Apply(healthy).Get<TenantPlacementPolicyV1>())).IsEqualTo(JsonData.Fingerprint(repaired));
        var healthyImage = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var healthyPosition = fixture.Store.Position;
        var healthyOutcome = TenantPlacementPolicyOutcomeAssertions.Stored(fixture, Healthy);
        fixture.Reopen();
        await TenantPlacementPolicyOutcomeAssertions.UnchangedAsync(fixture, healthyImage, healthyPosition, repaired);
        await TenantPlacementPolicyOutcomeAssertions.RefusalAsync(fixture, stale, bytes);
        await Assert.That(JsonData.Fingerprint(fixture.Database.Apply(healthy).Get<TenantPlacementPolicyV1>())).IsEqualTo(JsonData.Fingerprint(repaired));
        await Assert.That(TenantPlacementPolicyOutcomeAssertions.Stored(fixture, Healthy).SequenceEqual(healthyOutcome)).IsTrue();
        await TenantPlacementPolicyOutcomeAssertions.UnchangedAsync(fixture, healthyImage, healthyPosition, repaired);
    }

    internal static TenantPlacementPolicyV1 Policy(long revision, long promote, long demote)
        => new(1, ControlPartition.TenantId, ControlPartition.DatabaseId, revision, true,
            promote, demote, 256 * 1024 * 1024, 2_000, 8_000, 8_500, true,
            [new(PhysicalOwnerDirectoryWholeFlow.Control.Owner.PhysicalShardId, 1)]);

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture, Guid id, ConfigurePlacementPolicyRequest request)
        => fixture.Database.CreateNativeOperation(OperationKind.ConfigurePlacementPolicy, id,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
}
