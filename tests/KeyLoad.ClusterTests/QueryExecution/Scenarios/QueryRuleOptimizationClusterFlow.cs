using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryRuleOptimizationClusterFlow
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
                QueryRuleOptimizationClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => QueryRuleOptimizationClusterSetup.RunAsync(fixture, root, includeNull, includeMissing, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, QueryRuleOptimizationClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await ReadAndRepairAsync(root, reader, state, warm, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static async Task ReadAndRepairAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueryRuleOptimizationClusterState state, bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterCurrent.ObserveAsync(root, state,
            () => QueryRuleOptimizationClusterCancellation.RunAsync(reader, state), token), failures);
        await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterCurrent.ObserveAsync(root, state,
            () => QueryRuleOptimizationClusterPages.RunAsync(reader, state, false, token), token), failures);
        if (warm)
        {
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterSetup.PolicyAsync(root, state, WithdrawnPolicyEpoch, false, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterCurrent.ObserveAsync(root, state,
                () => QueryRuleOptimizationClusterPages.RunAsync(reader, state, true, token), token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterSetup.PolicyAsync(root, state, RepairedPolicyEpoch, true, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryRuleOptimizationClusterCurrent.ObserveAsync(root, state,
                () => QueryRuleOptimizationClusterPages.RunAsync(reader, state, false, token), token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
