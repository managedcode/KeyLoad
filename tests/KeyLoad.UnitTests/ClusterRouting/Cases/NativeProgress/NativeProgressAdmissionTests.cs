namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
internal sealed class NativeProgressAdmissionTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public Task SignedOnlineFreshZeroReplayAndPartialTerminalKeepStrictNativeAdmission()
        => NativeOnlineAdmissionFlow.GrammarAsync(fixture);

    [Test]
    public Task CancelledOnlineFrameDoesNotAdvanceAcceptedShapeBeforeHealthyContinuation()
        => NativeOnlineAdmissionFlow.CancelledAsync(fixture);
}
