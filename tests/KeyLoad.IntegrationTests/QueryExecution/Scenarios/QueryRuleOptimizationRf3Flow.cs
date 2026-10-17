using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Flow
{
    private const long WithdrawnPolicyEpoch = 3L;
    private const long RepairedPolicyEpoch = 4L;
    private const int ColdCycles = 2;

    internal static async Task RunAsync(bool includeNull, bool includeMissing)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<QueryRuleOptimizationRf3Tests>(
                    nameof(QueryRuleOptimizationRf3Tests.MetadataRevisionIndexPlansKeepPublicResultsFreshUseReplayAndTwoCold));
                QueryRuleOptimizationRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => QueryRuleOptimizationRf3Setup.RunAsync(fixture, root, includeNull, includeMissing, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, QueryRuleOptimizationRf3State state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await ReadAndRepairAsync(root, reader, state, warm, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static async Task ReadAndRepairAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        QueryRuleOptimizationRf3State state, bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Current.ObserveAsync(root, state,
            () => QueryRuleOptimizationRf3Cancellation.RunAsync(reader, state), token), failures);
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Current.ObserveAsync(root, state,
            () => QueryRuleOptimizationRf3Pages.RunAsync(reader, state, false, token), token), failures);
        if (warm)
        {
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Setup.PolicyAsync(root, state, WithdrawnPolicyEpoch, false, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Current.ObserveAsync(root, state,
                () => QueryRuleOptimizationRf3Pages.RunAsync(reader, state, true, token), token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Setup.PolicyAsync(root, state, RepairedPolicyEpoch, true, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationRf3Current.ObserveAsync(root, state,
                () => QueryRuleOptimizationRf3Pages.RunAsync(reader, state, false, token), token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
