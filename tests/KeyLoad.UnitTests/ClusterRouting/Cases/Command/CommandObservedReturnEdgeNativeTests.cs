namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class CommandObservedReturnEdgeNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task LostActualClaimOrBusinessReturnPreservesOriginalEffectsAndReplayThenJoinsAllNativeOwners(bool business)
        => CommandObservedReturnEdgeFlow.RunAsync(business, TestContext.Current!.Execution.CancellationToken);
}
