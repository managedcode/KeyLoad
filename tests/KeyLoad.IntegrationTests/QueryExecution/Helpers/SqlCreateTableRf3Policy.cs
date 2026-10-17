using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Policy
{
    private const long DeniedEpoch = 3;
    private const long RepairedEpoch = 4;
    internal static async Task<SqlCreateTableRf3State> RunAsync(ClusterFixture fixture, SqlCreateTableRf3State state,
        KeyLoadClient administrator, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            var repairId = Guid.NewGuid();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var image = await SqlCreateTableRf3Images.ReadAsync(administrator, token);
                    var denied = state.Identity.Principal with
                    {
                        ClusterAdministrator = false,
                        PolicyEpoch = DeniedEpoch
                    };
                    await ConfigureAsync(administrator, denied, token);
                    await FenceAsync(caller, wire, state.CreatedId, token);
                    await FenceAsync(caller, wire, state.FailedId, token);
                    await SqlCreateTableRf3Images.RequireAsync(administrator, image, token);
                    var repaired = state.Identity.Principal with
                    {
                        PolicyEpoch = RepairedEpoch
                    };
                    await ConfigureAsync(administrator, repaired, token);
                    await FenceAsync(caller, wire, state.CreatedId, token);
                    await FenceAsync(caller, wire, state.FailedId, token);
                    await SqlCreateTableRf3Images.RequireAsync(administrator, image, token);
                    var request = SqlCreateTableRf3Literal.Request(repairId, SqlCreateTableRf3Literal.RepairSql);
                    var expected = SqlCreateTableRf3Literal.Definition(SqlCreateTableRf3Literal.RepairTable);
                    await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(caller.Sdk, request, token));
                    await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<ResourceDefinition>(caller.Mcp, request, token));
                    await SqlCreateTableRf3Wire.ArmAsync(wire, repairId, token);
                    await SqlRf3Protocol.EqualAsync(expected, await wire.ExecuteAsync<ResourceDefinition>(SqlCreateTableRf3Literal.RepairSql, [], prepared: true, token));
                    await SqlCreateTableRf3Images.RequireAsync(administrator, image, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return state with
            {
                EpochFenced = true,
                RepairId = repairId,
                Identity = state.Identity with
                {
                    Principal = state.Identity.Principal with
                    {
                        PolicyEpoch = RepairedEpoch
                    }
                }
            };
        }, token);
    private static async Task ConfigureAsync(KeyLoadClient administrator, PrincipalRecord expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    internal static async Task FenceAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire, Guid id, CancellationToken token)
    {
        var request = SqlCreateTableRf3Literal.Request(id, SqlCreateTableRf3Literal.Sql);
        await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, request, ErrorCode.PermissionDenied, token);
        await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, request, ErrorCode.PermissionDenied, dispatched: true, token);
        await SqlCreateTableRf3Wire.ArmAsync(wire, id, token);
        await SqlCreateTableRf3Failures.WireAsync(wire, SqlCreateTableRf3Literal.Sql, SqlCreateTableRf3Literal.PermissionState, token);
    }
}
