namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class ValueHeaderRefusalTests
{
    [Test]
    public Task AcHeader001InvalidMarkerNeverDisclosesOrSnapshotsAndRealReplayRepairs()
        => ValueHeaderFlow.RunAsync();
}
