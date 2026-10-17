using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresCompositionRf3Operations
{
    internal static SqlOperationRequest Public(CommandRequest command)
        => command.Mutations[0] is QueueToGraph forward
            ? DeclarativeCompositionSql.Forward(command, forward) with
            {
                QueryDialectVersion = forward.FromCollection is null ? PostgresRf3Protocol.ScalarDialect : PostgresRf3Protocol.JoinDialect
            }
            : DeclarativeCompositionSql.Reverse(command, (GraphToQueueMutation)command.Mutations[0]);

    internal static async Task<CommitReceipt> ApplyAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        CommandRequest command, CancellationToken token)
    {
        var receipt = await wire.ExecuteAsync<CommitReceipt>(PostgresCompositionRf3Protocol.Sql(command),
            PostgresCompositionRf3Protocol.Parameters(command), prepared: true, token);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await ReplayAsync(callers, wire, command, receipt, token);
        return receipt;
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var request = Public(command);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, request, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, request, token));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.CommitAsync(command, token)));
        foreach (var prepared in new[] { false, true })
        {
            await SqlRf3Protocol.EqualAsync(receipt, await wire.ExecuteAsync<CommitReceipt>(
                PostgresCompositionRf3Protocol.Sql(command), PostgresCompositionRf3Protocol.Parameters(command), prepared, token));
        }
    }

    internal static async Task RefusedAsync(RequestCqrsRf3Callers callers, PostgresRf3Caller wire,
        CommandRequest command, ErrorCode code, string sqlState, CancellationToken token)
    {
        var request = Public(command);
        var sdk = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched: true);
        await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(PostgresCompositionRf3Protocol.Sql(command),
            PostgresCompositionRf3Protocol.Parameters(command), prepared: true, token), sqlState, code);
    }

    internal static async Task<PrincipalRecord> RepairAsync(KeyLoadClient root, PrincipalRecord current,
        CancellationToken token)
    {
        var expected = current with
        {
            PolicyEpoch = checked(current.PolicyEpoch + EpochStep),
            Grants = current.Grants.Select(grant => grant.Resource == PostgresCompositionRf3Protocol.Graph
                ? grant with
                {
                    Capabilities = grant.Capabilities | Capability.GraphWrite
                } : grant).ToImmutableArray()
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        return actual;
    }

    private const long EpochStep = 1;
}
