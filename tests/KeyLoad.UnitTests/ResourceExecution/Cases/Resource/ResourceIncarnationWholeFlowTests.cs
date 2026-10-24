namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class ResourceIncarnationWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalCreationIdentitySurvivesWritesPolicyRepairReplayIndependentStoreAndTwoColdOwners(bool sql)
        => ResourceIncarnationOwner.RunAsync(fixture => ResourceIncarnationFlow.RunAsync(
            fixture, sql, TestContext.Current!.Execution.CancellationToken));
}
