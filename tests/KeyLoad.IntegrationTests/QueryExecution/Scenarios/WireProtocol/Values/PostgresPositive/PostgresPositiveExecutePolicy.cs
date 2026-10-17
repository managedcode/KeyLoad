using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPositiveExecutePolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state,
        KeyLoadClient root, Stream stream, CancellationToken token)
    {
        var statement = PostgresPortalOwnershipProtocol.FirstStatement;
        var portal = PostgresPortalOwnershipProtocol.OldPortal;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, statement, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, portal, statement, token);
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId, RelationalSqlRf3JoinBudgetData.LeftCollection, Capability.DocumentsRead),
                new(PostgresRf3State.Partition.DatabaseId, RelationalSqlRf3JoinBudgetData.RightCollection, Capability.DocumentsRead)]
        };
        await PersistAsync(root, denied, token);
        var before = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
        await PostgresPortalOwnershipReplies.RefusedAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPositiveExecuteFrames.Execute(portal, PostgresPositiveExecuteProtocol.OneRow), PostgresPortalOwnershipProtocol.PermissionDenied, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
        {
            var result = await reader.Sdk.ExecuteSqlAsync(state.Operation, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                state.Operation, token), ErrorCode.PermissionDenied, dispatched: true);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
        await PersistAsync(root, original with
        {
            PolicyEpoch = original.PolicyEpoch + RepairAdvance
        }, token);
        before = await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, portal, statement, token);
        await PostgresPositiveExecuteTrial.ExecuteAsync(stream, state, portal, PostgresPositiveExecuteProtocol.OneRow, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, statement, token);
        await global::TUnit.Assertions.Assert.That(await PostgresPortalOwnershipAssertions.ImageAsync(root, state, token)).IsEqualTo(before);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    private const long DenialAdvance = 1;
    private const long RepairAdvance = 2;
}
