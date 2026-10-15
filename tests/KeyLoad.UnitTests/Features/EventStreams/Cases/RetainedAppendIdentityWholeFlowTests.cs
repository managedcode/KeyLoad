namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class RetainedAppendIdentityWholeFlowTests
{
    [Test]
    [Arguments("foreign-locator")]
    [Arguments("foreign-record")]
    [Arguments("record-revision")]
    [Arguments("event-id")]
    public Task CorruptRetainedLocatorOrCanonicalRecordRefusesWithoutOutcomeThenExactRepairSameGuidTriadReplayAndTwoColdRemainHealthy(string kind)
        => RetainedAppendIdentityFlow.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);
}
