using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class RemoteAnnPublicationRf3Tests
{
    [Test]
    public async Task DifferentVoterSdkAndOfficialMcpObserveActualPublicationAcrossMovePolicyRepairAndTwoColdRoots()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(
                LocalRf3ImageSelection.Read(), register: true, remote: true, query: true, probe: true,
                protectedDocument: true)
            {
                AnnReadExecution = IntegrationExecutionOptions.QueryExecution(new QueryExecutionOptions
                { EnableApproximateSearch = true })
            }, caller.Token).ConfigureAwait(false);
            await RemoteAnnPublicationRf3Flow.RunAsync(wave, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is { } owned)
        {
            if (failures.Count > RemoteAnnPublicationRf3Protocol.EmptyCount)
            { owned.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
