using KeyLoad.Clustering;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnActiveIdentityAssertions
{
    internal static async Task<AnnMaintenanceRequest> CloneAsync(AnnMaintenanceRequest request)
    {
        var profile = request.MaintenanceProfile ?? throw new InvalidOperationException();
        var clone = request with
        {
            MaintenanceProfile = profile with
            {
                Space = profile.Space with
                {
                }
            }
        };
        await Assert.That(ReferenceEquals(request.Space, profile.Space)).IsTrue();
        await Assert.That(ReferenceEquals(clone.Space, clone.MaintenanceProfile!.Space)).IsFalse();
        await Assert.That(AnnMaintenanceRequestIdentity.Same(request, clone)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(request).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(clone))).IsFalse();
        return clone;
    }

    internal static async Task RefuseAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request)
    {
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var files = AnnFixtureFileSnapshot.Capture(database.Directory);
        var changed = request with
        {
            CommandId = Guid.NewGuid()
        };
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
            changed, AnnMaintenanceCapabilityKind.Verify)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo(AnnProtocol.Ownership);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
    }
}
