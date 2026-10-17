namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeValueHeaderRefusalTests
{
    [Test]
    public Task AcNativeHeader001InvalidNativeMarkerNeverDisclosesOrSnapshotsAndRealReplayRepairs()
        => NativeValueHeaderFlow.RunAsync();
}
