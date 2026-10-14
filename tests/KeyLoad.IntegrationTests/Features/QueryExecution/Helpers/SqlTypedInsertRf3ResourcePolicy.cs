using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3ResourcePolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlTypedInsertRf3State state, KeyLoadClient root, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var original = SqlTypedInsertRf3Literal.Definition;
                    var denied = original with
                    {
                        SchemaVersion = checked(original.SchemaVersion + NextVersion),
                        FieldPolicies = [new(SqlTypedInsertRf3Literal.TitlePath, SqlTypedInsertRf3Literal.Classification,
                            SqlTypedInsertRf3Literal.ReadGrant, SqlTypedInsertRf3Literal.UseGrant, SqlTypedInsertRf3Literal.MissingGrant)]
                    };
                    await ConfigureAsync(root, denied, original.SchemaVersion, token);
                    var before = await SqlTypedInsertRf3Images.ReadAsync(root, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Created, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Failed, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    await ConfigureAsync(root, original with { SchemaVersion = checked(denied.SchemaVersion + NextVersion) }, denied.SchemaVersion, token);
                    await SqlTypedInsertRf3Wave.ReplayAsync(caller, wire, state.Created, state.Receipt, token);
                    await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, state.Failed, ErrorCode.Conflict, token);
                    await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, state.Failed, ErrorCode.Conflict, dispatched: true, token);
                    await SqlTypedInsertRf3Wire.RefuseAsync(wire, state.Failed, SqlCreateTableRf3Literal.ConflictState, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    await SqlTypedInsertRf3Pages.RunAsync(caller, wire, state, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    private static async Task ConfigureAsync(KeyLoadClient root, ResourceDefinition definition, long expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(SqlTypedInsertRf3Literal.Partition.TenantId, SqlTypedInsertRf3Literal.Partition.DatabaseId,
                definition)
            { ExpectedSchemaVersion = expected }, token)));
    private const long NextVersion = 1;
}
