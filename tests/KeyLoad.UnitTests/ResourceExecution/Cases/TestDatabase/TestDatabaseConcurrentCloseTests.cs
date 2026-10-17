namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TestDatabaseConcurrentCloseTests
{
    private const bool JoinBeforeDispose = true;
    private const bool DisposeFromLiveOwner = false;

    [Test]
    [Arguments(JoinBeforeDispose)]
    [Arguments(DisposeFromLiveOwner)]
    public async Task NativeAcknowledgedFixtureConcurrentAndRepeatedSuccessfulCloseThenIndependentHealthyFixture(bool joinFirst)
        => await TestDatabaseConcurrentCloseFlow.RunAsync(joinFirst, TestContext.Current!.Execution.CancellationToken);
}
