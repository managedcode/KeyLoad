namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3ColdMigrationTests
{
    [Test]
    public async Task Native6Rpc1WorkloadUpgradesBeforeCurrentMixedImageRejectionAndRestart()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        await RequestCqrsRf3Epoch7Scenario.RunAsync(deadline.Token).ConfigureAwait(false);
    }
}
