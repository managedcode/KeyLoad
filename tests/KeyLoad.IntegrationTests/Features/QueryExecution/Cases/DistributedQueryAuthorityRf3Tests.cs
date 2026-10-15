using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class DistributedQueryAuthorityRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceResourceFenceDuringOriginalLeafDestinationDenyRepairWholeMergeReceiptsCancellationAndTwoColdOwners(bool useMcp)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token);
            await ServerFailureObserver.ObserveAsync(() => DistributedQueryAuthorityRun.RunAsync(wave, useMcp, caller.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
