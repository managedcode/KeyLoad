namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal sealed class GenuineStoredOutcomeFrameTests
{
    [Test]
    public async Task AcMtoken005And006ActualNative6OutcomeFrameIsReadWithoutRewrite()
    {
        await GenuineStoredOutcomeFrameTestSupport.RunAsync(
            GenuineStoredOutcomeFrameTestSupport.AssertPriorFrameAsync,
            TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcMtoken006PriorProbeRejectsMissingCommandIdentity()
    {
        await GenuineStoredOutcomeFrameTestSupport.RunAsync(
            GenuineStoredOutcomeFrameTestSupport.AssertMissingCommandRejectedAsync,
            TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcMtoken006TruncatedPriorFrameFailsClosed()
    {
        await GenuineStoredOutcomeFrameTestSupport.RunAsync(
            GenuineStoredOutcomeFrameTestSupport.AssertTruncatedFrameAsync,
            TestContext.Current!.Execution.CancellationToken);
    }
}
