using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupMatrixRf3Assertions
{
    internal static async Task RequireNoOriginalDisposalAsync(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeMarkerRecord primary)
    {
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = fixture.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var paths = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory)
                .Where(path => Path.GetFileName(path).StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix,
                    StringComparison.Ordinal));
            await RequireMarkersAsync(fixture, primary, paths);
        }
        var original = fixture.ArmFor(primary.ArmId);
        await Assert.That(original.ProducerDisposedSeen).IsFalse();
        await Assert.That(original.DisposedGateJoined).IsFalse();
        await Assert.That(original.Settled).IsFalse();
        await Assert.That(original.Retired).IsFalse();
    }

    private static async Task RequireMarkersAsync(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeMarkerRecord primary, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var marker = fixture.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
            if (marker.ArmId != primary.ArmId)
            { continue; }
            await Assert.That(marker.Phase == RequestCqrsProbePhase.ProducerDisposed).IsFalse();
            await Assert.That(marker.Outcome is RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled).IsFalse();
        }
    }

}
