namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "rf3")]
[NotInParallel]
internal sealed class AtomicPartitionPlacementPublicRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcPmap003And004SdkMcpCasAuthorizationAndSequentialVoterReopen()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        await new AtomicPartitionPlacementPublicRf3Workflow(fixture).RunAsync(deadline.Token).ConfigureAwait(false);
    }
}
