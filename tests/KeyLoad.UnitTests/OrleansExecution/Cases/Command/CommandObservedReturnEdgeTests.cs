namespace KeyLoad.UnitTests.OrleansExecution;

[NotInParallel]
internal sealed class CommandObservedReturnEdgeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task LostActualClaimOrBusinessReturnPreservesOriginalEffectsAndReplayThenJoinsAllOwners(bool business)
        => CommandObservedReturnEdgeFlow.RunAsync(business, TestContext.Current!.Execution.CancellationToken);
}
