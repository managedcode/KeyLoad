namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class StreamRecordKeyAuthorityTests
{
    [Test]
    [Arguments(1L)]
    [Arguments(3L)]
    public Task MiskeyedRetainedRevisionRefusesCompleteTraversalThenExactRepairColdCursorAndHealthyAppend(long revision)
        => StreamRecordKeyAuthorityFlow.RunAsync(revision, TestContext.Current!.Execution.CancellationToken);
    [Test]
    [Arguments(1L)]
    [Arguments(2L)]
    [Arguments(3L)]
    public Task MissingCanonicalRetainedRowRefusesItsOriginalScopeThenRepairColdAndHealthyRemainExact(long revision)
        => StreamRecordKeyAuthorityFlow.RunOwnedAsync(
            fixture => StreamRecordGapAuthorityFlow.RunAsync(fixture, revision, TestContext.Current!.Execution.CancellationToken),
            TestContext.Current!.Execution.CancellationToken);
}
