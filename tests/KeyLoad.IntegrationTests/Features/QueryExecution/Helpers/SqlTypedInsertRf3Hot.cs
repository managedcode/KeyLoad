using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Hot
{
    internal static async Task<SqlTypedInsertRf3State> SeedAsync(ClusterFixture fixture, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await PostgresRf3Seed.ConfigureAsync(root.Sdk, token);
            var definition = SqlTypedInsertRf3Literal.Definition;
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(SqlTypedInsertRf3Literal.Partition.TenantId, SqlTypedInsertRf3Literal.Partition.DatabaseId, definition), token)));
            var identity = await PostgresRf3Seed.IdentityAsync(root.Sdk, IdentityPrefix,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, SqlTypedInsertRf3Literal.Table);
            var principal = identity.Principal with
            {
                PolicyEpoch = checked(identity.Principal.PolicyEpoch + NextEpoch),
                FieldGrants = [SqlTypedInsertRf3Literal.ReadGrant, SqlTypedInsertRf3Literal.UseGrant, SqlTypedInsertRf3Literal.WriteGrant]
            };
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            await Assert.That(principal.ClusterAdministrator).IsFalse();
            identity = identity with { Principal = principal };
            var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
                new(PlacementVersion, SqlTypedInsertRf3Literal.Partition), token));
            return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async caller =>
            {
                var created = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(), SqlTypedInsertRf3Literal.Sql);
                var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, created, token);
                await ReceiptAsync(created, receipt, owner,
                    new(SqlTypedInsertRf3Literal.PutKind, SqlTypedInsertRf3Literal.Table, SqlTypedInsertRf3Literal.One, SqlTypedInsertRf3Literal.InitialRevision),
                    new(SqlTypedInsertRf3Literal.PutKind, SqlTypedInsertRf3Literal.Table, SqlTypedInsertRf3Literal.Two, SqlTypedInsertRf3Literal.InitialRevision));
                var failed = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(), SqlTypedInsertRf3Literal.Duplicate);
                var before = await SqlTypedInsertRf3Images.ReadAsync(root.Sdk, token);
                await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, failed, ErrorCode.Conflict, token);
                await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, failed, ErrorCode.Conflict, dispatched: true, token);
                await SqlTypedInsertRf3Images.RequireAsync(root.Sdk, before, token);
                return new SqlTypedInsertRf3State(identity, created, receipt, failed, owner);
            }, token);
        }, token);
    internal static async Task ReceiptAsync(SqlOperationRequest request, CommitReceipt receipt, AtomicPartitionPlacementResolution owner, params MutationReceipt[] expected)
    {
        await Assert.That(owner.Partition).IsEqualTo(request.Partition);
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoPosition);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(request.CommandId
            ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult),
            new(owner.Incarnation, request.Partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [.. expected], DurabilityProfile.QuorumProcessDurable), receipt);
    }
    internal const int PlacementVersion = 1;
    private const long NoPosition = 0;
    private const string IdentityPrefix = "insert-";
    private const long NextEpoch = 1;
}
