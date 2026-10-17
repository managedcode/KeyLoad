namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeDatabaseDuplicateActivationRf3Tests
{
    [Test]
    public async Task SimultaneousNativeDatabaseContextsFenceOldAuthorityThenColdStableReplayAndHealthyContinuation()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await NativeActivationDuplicateRf3Scenario.RunAsync(caller.Token).ConfigureAwait(false);
    }
}
