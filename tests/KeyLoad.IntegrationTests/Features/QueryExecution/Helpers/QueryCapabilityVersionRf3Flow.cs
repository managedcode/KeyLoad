using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Flow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(QueryCapabilityVersionRf3Requests.Limits(), new QueryExecutionOptions());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<QueryCapabilityVersionRf3WholeFlowTests>(
                    nameof(QueryCapabilityVersionRf3WholeFlowTests.ManifestVersionsAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners));
                QueryCapabilityVersionRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => QueryCapabilityVersionRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < 2; cold++)
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
    private static async Task WaveAsync(ClusterFixture fixture, QueryCapabilityVersionRf3State state, bool warm)
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
        QueryCapabilityVersionRf3State state, bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRf3Current.ObserveAsync(root, state, async () =>
        {
            await QueryCapabilityVersionRf3Refusals.RunAsync(reader, state, false, token);
            await QueryCapabilityVersionRf3Pages.RunAsync(reader, state, token);
        }, token), failures);
        if (warm)
        {
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRf3Setup.PolicyAsync(root, state, 2L, false, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRf3Current.ObserveAsync(root, state,
                () => QueryCapabilityVersionRf3Refusals.RunAsync(reader, state, true, token), token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRf3Setup.PolicyAsync(root, state, 3L, true, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionRf3Current.ObserveAsync(root, state,
                () => QueryCapabilityVersionRf3Pages.RunAsync(reader, state, token), token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
