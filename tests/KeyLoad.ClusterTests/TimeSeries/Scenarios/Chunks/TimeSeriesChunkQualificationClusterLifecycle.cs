using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterLifecycle
{
    internal static async Task AuthorityAsync(ClusterFixture fixture, TimeSeriesChunkQualificationClusterState state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterCurrent.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationClusterRefusals.RunAsync(reader, state, false, deadline.Token), deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterSetup.PolicyAsync(root, state, TimeSeriesChunkQualificationClusterData.WithdrawnPolicyEpoch, false, false, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterCurrent.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationClusterRefusals.RunAsync(reader, state, true, deadline.Token), deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterSetup.PolicyAsync(root, state, TimeSeriesChunkQualificationClusterData.RepairedPolicyEpoch, true, true, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterCurrent.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationClusterReads.TagRepairAsync(reader, state, deadline.Token), deadline.Token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }
    internal static async Task RetainAsync(ClusterFixture fixture, TimeSeriesChunkQualificationClusterState state)
    {
        await PageAsync(fixture, state, TimeSeriesChunkQualificationClusterData.Base, TimeSeriesChunkQualificationClusterData.PartialPhase, 1L, false);
        await PageAsync(fixture, state, TimeSeriesChunkQualificationClusterData.Until, TimeSeriesChunkQualificationClusterData.ExpiredPhase, 2L, true);
        await PageAsync(fixture, state, TimeSeriesChunkQualificationClusterData.Until, TimeSeriesChunkQualificationClusterData.ExpiredPhase, 3L, false);
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await TimeSeriesChunkQualificationClusterSetup.CommitAsync(root, state, new(Guid.NewGuid(), state.Partitions[TimeSeriesChunkQualificationClusterData.PrimaryScope],
                [new DropSampleChunkWindow(TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series,
                    state.Window, TimeSeriesChunkQualificationClusterData.MergedRevision)]),
                [new("dropSampleChunkWindow", TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series,
                    TimeSeriesChunkQualificationClusterData.DroppedRevision)], deadline.Token);
            state.Phase = TimeSeriesChunkQualificationClusterData.DroppedPhase;
            state.Outboxes[TimeSeriesChunkQualificationClusterData.PrimaryScope] = await TimeSeriesChunkQualificationClusterSetup.ReadOutboxAsync(root, state, 0, deadline.Token);
            return true;
        }, deadline.Token);
        await TimeSeriesChunkQualificationClusterFlow.WaveAsync(fixture, state);
    }
    private static async Task PageAsync(ClusterFixture fixture, TimeSeriesChunkQualificationClusterState state,
        DateTimeOffset before, int phase, long purged, bool more)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await TimeSeriesChunkQualificationClusterSetup.CommitAsync(root, state, new(Guid.NewGuid(), state.Partitions[TimeSeriesChunkQualificationClusterData.PrimaryScope],
                [new ExpireSamples(TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series, before, TimeSeriesChunkQualificationClusterData.OneRetentionDelete)]),
                [new("expireSamples", TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series, purged)], deadline.Token);
            state.Phase = phase;
            state.Purged = purged;
            state.HasMore = more;
            state.Outboxes[TimeSeriesChunkQualificationClusterData.PrimaryScope] = await TimeSeriesChunkQualificationClusterSetup.ReadOutboxAsync(root, state, 0, deadline.Token);
            return true;
        }, deadline.Token);
        await TimeSeriesChunkQualificationClusterFlow.WaveAsync(fixture, state);
    }
}
