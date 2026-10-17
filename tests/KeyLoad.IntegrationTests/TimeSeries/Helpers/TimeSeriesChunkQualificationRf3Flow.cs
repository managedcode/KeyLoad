using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Flow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<TimeSeriesChunkQualificationRf3Tests>(
                    nameof(TimeSeriesChunkQualificationRf3Tests.SameChunkIdentityAcrossAtomicPartitionsKeepsExactValuesOnRetentionDropReplayAndTwoCold));
                TimeSeriesChunkQualificationRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => TimeSeriesChunkQualificationRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WarmAsync(fixture, state), failures);
                for (var cold = 0; cold < TimeSeriesChunkQualificationRf3Data.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, TimeSeriesChunkQualificationRf3State state)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Lifecycle.AuthorityAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Lifecycle.RetainAsync(fixture, state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task WaveAsync(ClusterFixture fixture, TimeSeriesChunkQualificationRf3State state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await TimeSeriesChunkQualificationRf3Current.ObserveAsync(root, state, async () =>
                {
                    await TimeSeriesChunkQualificationRf3Refusals.CancellationAsync(reader, state);
                    await TimeSeriesChunkQualificationRf3Refusals.BoundAsync(reader, state, deadline.Token);
                    await TimeSeriesChunkQualificationRf3Reads.RunAsync(reader, state, true, deadline.Token);
                }, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
}
