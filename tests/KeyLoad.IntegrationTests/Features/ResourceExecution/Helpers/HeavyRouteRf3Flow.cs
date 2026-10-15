using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Flow
{
    private static HttpAdmissionLimits Limits() => new()
    {
        MaxReservedBytes = 130_000,
        HeavyReadReservedBytes = 65_536,
        OtherReservedBytes = 0,
        MaxBodyBytes = 8_192,
        MaxControlBodyBytes = 1_024,
        ReservedControlBytes = 20_000
    };
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(Limits(), new DatabaseLimits(), new QueryExecutionOptions { EnableApproximateSearch = true });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<HeavyRouteAdmissionRf3Tests>(
                    nameof(HeavyRouteAdmissionRf3Tests.ActualHeldBodyRejectsFourHeavySdkMcpRoutesButKeepsControlThenFreshRepairReplayAndTwoCold));
                using var deadline = McpCallerDeadline.Create();
                var state = await RootAsync(fixture, root => HeavyRouteRf3Seed.RunAsync(root, deadline.Token), deadline.Token);
                await WarmAsync(fixture, state, deadline.Token);
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var phase = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, phase.Token);
                        await WaveAsync(fixture, state, null, phase.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, HeavyRouteRf3State state, CancellationToken token)
        => await RootAsync(fixture, async root => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App,
            McpCallerProtocol.Node1, state.Key, async reader =>
            {
                await HeavyRouteRf3Held.RunAsync(fixture, root, reader, state, token);
                await HeavyRouteRf3Operations.RunAsync(root, reader, state, null, token);
                await HeavyRouteRf3Seed.PersistAsync(root, state, 2L, false, token);
                await HeavyRouteRf3Operations.RunAsync(root, reader, state, ErrorCode.PermissionDenied, token);
                await HeavyRouteRf3Seed.PersistAsync(root, state, 3L, true, token);
                await HeavyRouteRf3Operations.RunAsync(root, reader, state, null, token);
                await HeavyRouteRf3Current.ReplayAsync(root, state, token);
                return true;
            }, token), token);
    private static async Task WaveAsync(ClusterFixture fixture, HeavyRouteRf3State state, ErrorCode? error, CancellationToken token)
        => await RootAsync(fixture, async root => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App,
            McpCallerProtocol.Node1, state.Key, async reader =>
            {
                await HeavyRouteRf3Operations.RunAsync(root, reader, state, error, token);
                await HeavyRouteRf3Current.ReplayAsync(root, state, token);
                return true;
            }, token), token);
    private static Task<T> RootAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, operation, token);
}
