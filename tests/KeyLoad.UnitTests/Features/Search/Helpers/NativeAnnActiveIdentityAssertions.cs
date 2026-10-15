using KeyLoad.Clustering;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnActiveIdentityAssertions
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
        await Assert.That(NativeSerialization.Serialize(request).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(clone))).IsFalse();
        return clone;
    }

    internal static async Task RefuseAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request)
    {
        var before = NativeAnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
        var changed = request with
        {
            CommandId = Guid.NewGuid()
        };
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
            changed, AnnMaintenanceCapabilityKind.Verify)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo(NativeAnnProtocol.Ownership);
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
    }
}
