namespace KeyLoad.FunctionalTests.OrleansExecution;

[RequestCqrsHostedDataSource]
internal sealed class ProgressAdmissionTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task SignedOnlineFreshZeroReplayAndPartialTerminalKeepStrictAdmission()
        => OnlineAdmissionFlow.GrammarAsync(fixture);

    [Test]
    public Task CancelledOnlineFrameDoesNotAdvanceAcceptedShapeBeforeHealthyContinuation()
        => OnlineAdmissionFlow.CancelledAsync(fixture);
}
