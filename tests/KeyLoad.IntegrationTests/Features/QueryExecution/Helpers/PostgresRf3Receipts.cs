using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Receipts
{
    private const string DifferentJson = "{\"customer_id\":\"customer-native\",\"detail\":\"changed\",\"order_id\":\"order-native\"}";

    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var before = await PostgresRf3Assertions.ImageAsync(root.Sdk, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Writer.Secret, async writer =>
            {
                await SqlRf3Protocol.EqualAsync(state.Receipt,
                    await McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(state.Seed, token)));
                await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await writer.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
                var failures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                        state.Writer.Principal.Id, state.Writer.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await SqlRf3Protocol.EqualAsync(state.Receipt, await wire.CommitAsync(state.Seed, true, token));
                        await SqlRf3Protocol.EqualAsync(state.Receipt,
                            await wire.CommitAsync(state.Seed, false, token, global::NpgsqlTypes.NpgsqlDbType.Json));
                        var malformed = new global::Npgsql.NpgsqlParameter
                        {
                            NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb,
                            Value = MalformedJson
                        };
                        await PostgresRf3Assertions.ErrorAsync(
                            wire.ExecuteAsync<CommitReceipt>(PostgresRf3Protocol.CommitSql, [malformed], false, token),
                            ValidationSqlState, ErrorCode.Validation);
                        await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root.Sdk, token)).IsEqualTo(before);
                        await SqlRf3Protocol.EqualAsync(state.Receipt, await wire.CommitAsync(state.Seed, false, token));
                        var conflict = state.Seed with
                        {
                            Mutations = [new PutDocument(PostgresRf3State.Left.Collection,
                            PostgresRf3State.Left.Id, DifferentJson)]
                        };
                        await PostgresRf3Assertions.ErrorAsync(wire.CommitAsync(conflict, false, token),
                            PostgresRf3Protocol.ConflictSqlState, ErrorCode.Conflict);
                        await SqlRf3Protocol.EqualAsync(state.Receipt, await wire.CommitAsync(state.Seed, false, token));
                        await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(UnsupportedSql, [], false, token),
                            PostgresRf3Protocol.UnsupportedSqlState, ErrorCode.UnsupportedCapability);
                        await PostgresRf3Assertions.PageAsync(state,
                            await wire.ExecuteAsync<QueryPage>(state.Sql, [], true, token));
                    }, failures);
                }, failures);
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root.Sdk, token)).IsEqualTo(before);
            return true;
        }, token);
    }

    private const string UnsupportedSql = "BEGIN";
    private const string ValidationSqlState = "22023";
    private const string MalformedJson = "{";
}
