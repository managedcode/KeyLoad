namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsHostedDataSource]
internal sealed class NativeProgressAdmissionTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task SignedOnlineFreshZeroReplayAndPartialTerminalKeepStrictNativeAdmission()
        => NativeOnlineAdmissionFlow.GrammarAsync(fixture);

    [Test]
    public Task CancelledOnlineFrameDoesNotAdvanceAcceptedShapeBeforeHealthyContinuation()
        => NativeOnlineAdmissionFlow.CancelledAsync(fixture);
}
