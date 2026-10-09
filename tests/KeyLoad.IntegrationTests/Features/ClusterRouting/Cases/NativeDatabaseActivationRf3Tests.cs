namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeDatabaseActivationRf3Tests
{
    [Test]
    public async Task SameNativeDatabaseGrainReplacesActivationReplaysStableCommandThenColdHealthyContinuation()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        await NativeActivationRf3Scenario.RunAsync(caller.Token).ConfigureAwait(false);
    }
}
