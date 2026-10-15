using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CatalogBootstrapAuthorityFlow
{
    internal static async Task RunAsync(string variant)
    {
        var failures = new List<Exception>();
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        var token = deadline.Token;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var state = await WarmAsync(fixture, variant, token);
                for (var cold = P.FirstIndex; cold < P.ColdCycles; cold++)
                {
                    await ModelSqlRf3Cold.RunAsync(fixture, token);
                    await CatalogBootstrapAuthorityCurrent.RunAsync(fixture, state, token);
                    await CatalogBootstrapAuthorityDenied.ColdRootAsync(fixture, state, variant, token);
                    if (cold == P.FirstIndex)
                    {
                        await HealthyAsync(fixture, state, token);
                    }
                    await CatalogBootstrapAuthorityCurrent.RunAsync(fixture, state, token);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<CatalogBootstrapAuthorityState> WarmAsync(ClusterFixture fixture,
        string variant, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, P.Nodes[P.FirstIndex], fixture.AdminKey, async root =>
        {
            var state = await CatalogBootstrapAuthoritySeed.RunAsync(fixture, root, token);
            await CatalogBootstrapAuthorityCurrent.RunAsync(fixture, state, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, P.Nodes[P.SecondIndex], state.AdministratorKey, async survivor =>
            {
                await CatalogBootstrapAuthorityChange.RunAsync(fixture, survivor, variant, token);
                await CatalogBootstrapAuthorityDenied.RootAsync(root, state, variant, token);
                return true;
            }, token);
            await CatalogBootstrapAuthorityCurrent.RunAsync(fixture, state, token);
            return state;
        }, token);

    private static async Task HealthyAsync(ClusterFixture fixture, CatalogBootstrapAuthorityState state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, P.Nodes[P.SecondIndex], state.AdministratorKey, async survivor =>
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(P.Collection, P.Document, P.HealthyJson, P.OriginalRevision)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await survivor.Sdk.CommitAsync(command, token));
            await CatalogBootstrapAuthorityCurrent.ReceiptAsync(command, receipt, state.Placement, P.HealthyRevision);
            state.HealthyCommand = command;
            state.HealthyReceipt = receipt;
            return true;
        }, token);
}
