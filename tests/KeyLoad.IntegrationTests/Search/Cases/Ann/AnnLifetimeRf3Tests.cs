using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class AnnLifetimeRf3Tests
{
    [Test]
    public async Task ActualAnnDataRepliesSourcePolicyFenceCancellationRepairReceiptsAndTwoColdSdkMcpOwners()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(
                LocalRf3ImageSelection.Read(), register: true, remote: true, query: true, probe: true, protectedDocument: true)
            {
                AnnReadExecution = IntegrationExecutionOptions.QueryExecution(new QueryExecutionOptions { EnableApproximateSearch = true })
            }, caller.Token);
            await AnnLifetimeRf3Flow.RunAsync(wave, caller.Token);
        }, failures);
        if (wave is { } owned)
        {
            if (failures.Count != default)
            {
                owned.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
