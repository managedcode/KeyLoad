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

    [Test]
    public Task SignedPlacementFreshResumeZeroReplayAndPartialTerminalKeepStrictNativeAdmission()
        => NativePlacementAdmissionFlow.GrammarAsync(fixture);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task RejectedNativeFrameOrAggregateBytesDoNotAdvancePlacementRevisionOrMoveSet(bool aggregate)
        => NativePlacementAdmissionFlow.BoundContinuationAsync(fixture, aggregate);

    [Test]
    public Task ExistingTextRejectsMixedPlacementAndStartedRejectsPayloadBeforeHealthyContinuation()
        => NativeProgressAdmissionSupport.MixedTextAsync(fixture);
}
