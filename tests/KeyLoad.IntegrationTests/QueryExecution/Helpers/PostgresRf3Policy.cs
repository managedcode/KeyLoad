using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Policy
{
    private const long DenialAdvance = 1;
    private const long RepairAdvance = 2;

    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async reader =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresRf3Assertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                    var original = state.Reader.Principal;
                    await PostgresDefaultJoinCalls.RefuseRepairAsync(state, wire, reader, root, token);
                    var denied = original with
                    {
                        PolicyEpoch = original.PolicyEpoch + DenialAdvance,
                        Grants = [new(PostgresRf3State.Partition.DatabaseId,
                            RelationalSqlRf3JoinBudgetData.LeftCollection, Capability.DocumentsRead),
                            new(PostgresRf3State.Partition.DatabaseId,
                            RelationalSqlRf3JoinBudgetData.RightCollection, Capability.DocumentsRead)]
                    };
                    await PersistAsync(root, denied, token);
                    var before = await PostgresRf3Assertions.ImageAsync(root, token);
                    await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: false, token),
                        PostgresRf3Protocol.PermissionSqlState, ErrorCode.PermissionDenied);
                    var sdk = await reader.Sdk.ExecuteSqlAsync(state.Operation, token);
                    await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
                    await global::TUnit.Assertions.Assert.That(sdk.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
                    await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
                    await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                        state.Operation, token), ErrorCode.PermissionDenied, dispatched: true);
                    await PostgresDefaultJoinCalls.DeniedAsync(state, reader, wire, token);
                    await SqlTableRf3Refusals.DeniedAsync(state, reader, wire, token);
                    await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
                    await PersistAsync(root, original with
                    {
                        PolicyEpoch = original.PolicyEpoch + RepairAdvance
                    }, token);
                    await PostgresDefaultJoinCalls.HealthyAsync(state, reader, wire, token);
                    before = await PostgresRf3Assertions.ImageAsync(root, token);
                    await PostgresRf3Assertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                    await PostgresRf3Assertions.PageAsync(state,
                        await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, state.Operation, token));
                    await PostgresRf3Assertions.PageAsync(state,
                        await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, state.Operation, token));
                    await SqlTableRf3Pages.RequireAsync(state, reader, wire, token);
                    await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
                    await PostgresRf3Credential.RunAsync(root, reader, wire, state, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
