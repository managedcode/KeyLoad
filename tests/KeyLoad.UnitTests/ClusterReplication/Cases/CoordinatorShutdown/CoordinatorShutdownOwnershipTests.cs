namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class CoordinatorShutdownOwnershipTests
{
    [Test]
    public async Task NativeCancellationFileFailureCannotBypassOriginalWorkerThenHealthyReplaySurvivesTwoColdOpens()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        await CoordinatorShutdownFlow.RunAsync(caller.Token);
    }
}
