using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextCalls
{
    internal static Task<(Result<QueryPage> Result, ConnectionProbeWitness Witness, RequestCqrsProbeMarkerRecord Marker)> SdkAsync(
        ConnectionRf3Scenario scenario, KeyLoadClient sdk, RequestCqrsPhaseFaultIdentity identity,
        int route, string? cursor, CancellationToken token)
        => scenario.ObserveAsync(identity, Guid.Empty, route == 0 ? GrainReadKind.Query : GrainReadKind.AstQuery,
            () => ExecuteAsync(sdk, identity.Partition, route, cursor, token), token);
    internal static Task<Result<QueryPage>> ExecuteAsync(KeyLoadClient sdk, PartitionRef partition,
        int route, string? cursor, CancellationToken token)
        => route == 0 ? sdk.QueryAsync(ConnectionQueryContextRequests.Sql(partition, cursor), token)
            : sdk.QueryAstAsync(route == 1 ? ConnectionQueryContextRequests.Ast(partition, cursor)
                : ConnectionQueryContextRequests.CSharp(partition, cursor), token);
    internal static async Task<ConnectionQueryContextRead> SuccessAsync(ConnectionRf3Scenario scenario, KeyLoadClient sdk,
        RequestCqrsPhaseFaultIdentity identity, int route, string? cursor, CancellationToken token)
    {
        var original = await SdkAsync(scenario, sdk, identity, route, cursor, token);
        return new(await McpCallerAssertions.SdkSuccessAsync(original.Result), original.Witness, original.Marker);
    }
    internal static async Task<ConnectionQueryContextRead> McpAsync(ConnectionRf3Scenario scenario, ConnectionRf3Caller caller,
        RequestCqrsPhaseFaultIdentity identity, int route, string? cursor, CancellationToken token)
    {
        var original = await scenario.ObserveAsync(identity, Guid.Empty,
            route == 0 ? GrainReadKind.Query : GrainReadKind.AstQuery,
            () => route == 0 ? caller.CallAsync(McpCallerTools.QueryExecute, ConnectionQueryContextRequests.Sql(identity.Partition, cursor), token)
                : caller.CallAsync(McpCallerTools.QueryAst, route == 1 ? ConnectionQueryContextRequests.Ast(identity.Partition, cursor)
                    : ConnectionQueryContextRequests.CSharp(identity.Partition, cursor), token), token);
        return new((await McpCallerAssertions.SuccessAsync<QueryPage>(original.Result)).Value, original.Witness, original.Marker);
    }
    internal static async Task ErrorAsync(Result<QueryPage> original, ErrorCode expected, string? detail = null)
    {
        await Assert.That(original.IsSuccess).IsFalse();
        await Assert.That(original.Value).IsNull();
        await Assert.That(original.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        if (detail is not null)
        {
            await SqlRf3Protocol.EqualAsync(Errors.Problem(expected, detail), original.Problem);
        }
    }
}
