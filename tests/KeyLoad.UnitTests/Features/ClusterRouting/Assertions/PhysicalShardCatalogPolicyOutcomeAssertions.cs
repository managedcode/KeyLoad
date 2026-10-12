using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogPolicyOutcomeAssertions
{
    private const int CatalogVersion = 1;
    private const long InitialRevision = 1;
    private const long InitialPlacementEpoch = 1;
    private const long InitialPolicyEpoch = 1;
    private const string MissingOutcome = "The original catalog command outcome is missing.";
    private const string PolicyChanged = "The principal policy changed since this command was evaluated.";
    private const string CompleteImageRequired = "The catalog outcome flow requires its complete bounded canonical image.";

    internal static byte[] Image(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view =>
        {
            var page = view.Scan([], fixture.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            { throw new InvalidOperationException(CompleteImageRequired); }
            return NativeSerialization.Serialize(page);
        });

    internal static byte[] Outcome(PhysicalShardCatalogFixture fixture, Guid id)
        => fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(
            PhysicalShardCatalogPolicyOutcomeFlow.PrincipalId, id))
            ?? throw new InvalidOperationException(MissingOutcome));

    internal static async Task UnchangedAsync(PhysicalShardCatalogFixture fixture, byte[] image, long cut)
    {
        await Assert.That(Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
    }

    internal static async Task AbsentAsync(PhysicalShardCatalogFixture fixture)
        => await Assert.That(fixture.Store.Read(view =>
            view.ReadOwnedValue(PhysicalShardCatalogRecordSerialization.CatalogKey()))).IsNull();

    internal static async Task StoredRefusalAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, OperationResult result, byte[] bytes)
    {
        await Assert.That(Outcome(fixture, original.Id).SequenceEqual(bytes)).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(bytes);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(NativeSerialization.Serialize(stored.Result).SequenceEqual(
            NativeSerialization.Serialize(result))).IsTrue();
    }

    internal static async Task PolicyReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] bytes)
    {
        var replay = fixture.Database.Apply(original);
        await Assert.That(replay.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(replay.SafeDetail).IsEqualTo(PolicyChanged);
        await Assert.That(replay.Json).IsNull();
        await Assert.That(replay.NativeValue).IsNull();
        await Assert.That(Outcome(fixture, original.Id).SequenceEqual(bytes)).IsTrue();
    }

    internal static async Task HealthyAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] receipt)
    {
        var replay = fixture.Database.Apply(original);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.Get<bool>()).IsTrue();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(receipt)).IsTrue();
        var request = PhysicalShardCatalogPolicyOutcomeFlow.Request();
        var expected = new PhysicalShardCatalog(CatalogVersion, InitialRevision,
            new(request.PhysicalShardId, request.Incarnation, request.VoterIds, InitialPlacementEpoch));
        var actual = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogPolicyOutcomeFlow.PrincipalId);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }
}
