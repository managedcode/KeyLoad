using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class QueryContextSessionRefusals
{
    private const string ScanDenied = "The query requires an index or explicit full-scan permission.";
    private const string StructureBudgetExceeded = "The query structure exceeds its budget.";

    private const int DefaultRows = 1_000;
    internal static async Task RunAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        KeyLoadClient second, ConnectionProbeWitness owner, CancellationToken token)
    {
        var manifest = await McpCallerAssertions.SdkSuccessAsync(await second.QueryCapabilitiesAsync(token));
        await Assert.That(manifest.MaxRows).IsEqualTo(DefaultRows);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var actual = await scenario.ObserveAsync(state.Second.Identity, Guid.Empty, GrainReadKind.Query,
                () => second.QueryAsync(ConnectionQueryContextRequests.Sql(state.Second.Identity.Partition) with
                {
                    AllowFullScan = false
                }, token), token);
            await ErrorAsync(actual.Result, ErrorCode.UnsupportedCapability, ScanDenied);
            await ConnectionRf3WitnessReader.SameOwnerAsync(owner, actual.Witness);
        }, token);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var request = ConnectionQueryContextRequests.Ast(state.Second.Identity.Partition);
            request = request with
            {
                Query = request.Query with
                {
                    Offset = DefaultRows
                }
            };
            var actual = await scenario.ObserveAsync(state.Second.Identity, Guid.Empty, GrainReadKind.AstQuery,
                () => second.QueryAstAsync(request, token), token);
            await ErrorAsync(actual.Result, ErrorCode.BudgetExceeded, StructureBudgetExceeded);
            await ConnectionRf3WitnessReader.SameOwnerAsync(owner, actual.Witness);
        }, token);
        await ConnectionQueryContextPages.SdkAsync(scenario, state, second, state.Second, owner, token);
    }
    private static async Task ErrorAsync(ManagedCode.Communication.Result<QueryPage> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
    }
}
