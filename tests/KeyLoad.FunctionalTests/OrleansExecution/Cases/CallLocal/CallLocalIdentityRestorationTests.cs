namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CallLocalIdentityRestorationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ConcurrentCommandsRestoreDistinctPriorContextsAfterCancellation(bool firstPresentNull)
        => CallLocalIdentityRestorationFlow.RunAsync(firstPresentNull, TestContext.Current!.Execution.CancellationToken);
}
