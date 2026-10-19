using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

[NotInParallel]
internal sealed class RequestCqrsOfflineProcessTests
{
    [Test]
    public Task BoundedUtf8ChildCompletesWithExactInclusiveOutput()
        => RequestCqrsOfflineProcessTestScope.RunAsync(async scope =>
        {
            var result = await scope.StartSuccessAsync().ConfigureAwait(false);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Stdout).IsEqualTo(new string('A', OwnedChildProcessProtocol.MaximumOutputBytes));
            await Assert.That(result.Stderr).IsEqualTo(RequestCqrsOfflineProcessProtocol.SuccessError);
        }, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CancellingLiveChildJoinsExitAndBothReaders()
        => RequestCqrsOfflineProcessTestScope.RunAsync(async scope =>
        {
            var child = scope.StartCancellation();
            var process = await child.WaitForStartedAsync().ConfigureAwait(false);
            await child.CancelAsync().ConfigureAwait(false);
            var failure = await child.ExpectCancellationAsync().ConfigureAwait(false);
            await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
            await RequestCqrsOfflineProcessAssertions.AssertExitedAsync(process).ConfigureAwait(false);
            var healthy = await scope.StartSuccessAsync().ConfigureAwait(false);
            await Assert.That(healthy.ExitCode).IsEqualTo(0);
        }, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ExcessOutputKillsAndJoinsOwnedChild()
        => RequestCqrsOfflineProcessTestScope.RunAsync(async scope =>
        {
            var child = scope.StartOversized();
            var process = await child.WaitForStartedAsync().ConfigureAwait(false);
            await child.ReleaseOutputAsync().ConfigureAwait(false);
            var failure = await child.ExpectOutputLimitAsync().ConfigureAwait(false);
            await Assert.That(failure.Message).IsEqualTo(OwnedChildProcessProtocol.OutputExceeded);
            await RequestCqrsOfflineProcessAssertions.AssertExitedAsync(process).ConfigureAwait(false);
            var healthy = await scope.StartSuccessAsync().ConfigureAwait(false);
            await Assert.That(healthy.ExitCode).IsEqualTo(0);
        }, TestContext.Current!.Execution.CancellationToken);
}
