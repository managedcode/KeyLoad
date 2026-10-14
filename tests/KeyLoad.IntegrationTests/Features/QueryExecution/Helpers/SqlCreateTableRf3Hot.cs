using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Hot
{
    internal static async Task<SqlCreateTableRf3State> SeedAsync(ClusterFixture fixture, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await PostgresRf3Seed.ConfigureAsync(root.Sdk, token);
            var identity = await PostgresRf3Seed.IdentityAsync(root.Sdk, "ddl-",
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, SqlCreateTableRf3Literal.Table);
            var principal = identity.Principal with
            {
                ClusterAdministrator = true,
                PolicyEpoch = SqlCreateTableRf3Literal.AdministratorEpoch
            };
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            identity = identity with
            {
                Principal = principal
            };
            return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async caller =>
            {
                var createdId = Guid.NewGuid();
                var request = SqlCreateTableRf3Literal.Request(createdId, SqlCreateTableRf3Literal.Sql);
                var expected = SqlCreateTableRf3Literal.Definition(SqlCreateTableRf3Literal.Table);
                await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(caller.Sdk, request, token));
                await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<ResourceDefinition>(caller.Mcp, request, token));
                var before = await SqlCreateTableRf3Images.ReadAsync(root.Sdk, token);
                await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, new(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.Sql), ErrorCode.Validation, token);
                await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, new(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.Sql), ErrorCode.Validation, dispatched: false, token);
                await SqlCreateTableRf3Images.RequireAsync(root.Sdk, before, token);
                var failedId = Guid.NewGuid();
                var duplicate = SqlCreateTableRf3Literal.Request(failedId, SqlCreateTableRf3Literal.Sql);
                await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, duplicate, ErrorCode.Conflict, token);
                await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, duplicate, ErrorCode.Conflict, dispatched: true, token);
                await SqlCreateTableRf3Images.RequireAsync(root.Sdk, before, token);
                var invalidId = Guid.NewGuid();
                var invalid = new CommandRequest(invalidId, SqlCreateTableRf3Literal.Partition,
                    [new PutDocument(SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One, SqlCreateTableRf3Literal.InvalidRow)]);
                var rejected = await caller.Sdk.CommitAsync(invalid, token);
                await Assert.That(rejected.IsSuccess).IsFalse();
                await Assert.That(rejected.Problem).IsNotNull();
                await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
                await SqlCreateTableRf3Images.RequireAsync(root.Sdk, before, token);
                var seed = new CommandRequest(Guid.NewGuid(), SqlCreateTableRf3Literal.Partition,
                    [new PutDocument(SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One, SqlCreateTableRf3Literal.Row)]);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed, token));
                await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
                await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
                await SqlRf3Protocol.EqualAsync(new MutationReceipt[] { new("putDocument", SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One, SqlCreateTableRf3Literal.InitialRevision) }, receipt.Mutations.ToArray());
                return new SqlCreateTableRf3State(identity, createdId, failedId, expected, seed, receipt);
            }, token);
        }, token);
}
