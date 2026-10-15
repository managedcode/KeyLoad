using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Lifecycle
{
    internal static async Task AuthorityAsync(ClusterFixture fixture, TimeSeriesChunkQualificationRf3State state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Current.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationRf3Refusals.RunAsync(reader, state, false, deadline.Token), deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Setup.PolicyAsync(root, state, TimeSeriesChunkQualificationRf3Data.WithdrawnPolicyEpoch, false, false, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Current.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationRf3Refusals.RunAsync(reader, state, true, deadline.Token), deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Setup.PolicyAsync(root, state, TimeSeriesChunkQualificationRf3Data.RepairedPolicyEpoch, true, true, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Current.ObserveAsync(root, state,
                    () => TimeSeriesChunkQualificationRf3Reads.TagRepairAsync(reader, state, deadline.Token), deadline.Token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }
    internal static async Task RetainAsync(ClusterFixture fixture, TimeSeriesChunkQualificationRf3State state)
    {
        await PageAsync(fixture, state, TimeSeriesChunkQualificationRf3Data.Base, TimeSeriesChunkQualificationRf3Data.PartialPhase, 1L, false);
        await PageAsync(fixture, state, TimeSeriesChunkQualificationRf3Data.Until, TimeSeriesChunkQualificationRf3Data.ExpiredPhase, 2L, true);
        await PageAsync(fixture, state, TimeSeriesChunkQualificationRf3Data.Until, TimeSeriesChunkQualificationRf3Data.ExpiredPhase, 3L, false);
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await TimeSeriesChunkQualificationRf3Setup.CommitAsync(root, state, new(Guid.NewGuid(), state.Partitions[TimeSeriesChunkQualificationRf3Data.PrimaryScope],
                [new DropSampleChunkWindow(TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series,
                    state.Window, TimeSeriesChunkQualificationRf3Data.MergedRevision)]),
                [new("dropSampleChunkWindow", TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series,
                    TimeSeriesChunkQualificationRf3Data.DroppedRevision)], deadline.Token);
            state.Phase = TimeSeriesChunkQualificationRf3Data.DroppedPhase;
            state.Outboxes[TimeSeriesChunkQualificationRf3Data.PrimaryScope] = await TimeSeriesChunkQualificationRf3Setup.ReadOutboxAsync(root, state, 0, deadline.Token);
            return true;
        }, deadline.Token);
        await TimeSeriesChunkQualificationRf3Flow.WaveAsync(fixture, state);
    }
    private static async Task PageAsync(ClusterFixture fixture, TimeSeriesChunkQualificationRf3State state,
        DateTimeOffset before, int phase, long purged, bool more)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await TimeSeriesChunkQualificationRf3Setup.CommitAsync(root, state, new(Guid.NewGuid(), state.Partitions[TimeSeriesChunkQualificationRf3Data.PrimaryScope],
                [new ExpireSamples(TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series, before, TimeSeriesChunkQualificationRf3Data.OneRetentionDelete)]),
                [new("expireSamples", TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series, purged)], deadline.Token);
            state.Phase = phase;
            state.Purged = purged;
            state.HasMore = more;
            state.Outboxes[TimeSeriesChunkQualificationRf3Data.PrimaryScope] = await TimeSeriesChunkQualificationRf3Setup.ReadOutboxAsync(root, state, 0, deadline.Token);
            return true;
        }, deadline.Token);
        await TimeSeriesChunkQualificationRf3Flow.WaveAsync(fixture, state);
    }
}
