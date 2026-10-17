using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementSeed
{
    internal static async Task<SqlTypedInsertMovementState> RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var definition = SqlTypedInsertMovementProtocol.Definition(seed.Partition);
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigureResourceAsync(
            Guid.NewGuid(), new(seed.Partition.TenantId, seed.Partition.DatabaseId, definition), token)));
        var principal = new PrincipalRecord("moved-sql-writer-" + Guid.NewGuid().ToString("N"), seed.Partition.TenantId,
            [new(seed.Partition.DatabaseId, SqlTypedInsertMovementProtocol.Table, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)], []);
        var id = "moved-sql-key-" + Guid.NewGuid().ToString("N");
        var secret = id + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SqlTypedInsertMovementProtocol.SecretBytes));
        var credential = new ApiKeyRecord(id, principal.Id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        foreach (var root in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
            await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token));
        }
        await Assert.That(principal.ClusterAdministrator).IsFalse();
        var identity = new McpPersistedIdentity(principal, credential, secret);
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, secret, async caller =>
        {
            var request = SqlTypedInsertMovementProtocol.Request(seed.Partition, SqlTypedInsertMovementProtocol.OriginalId,
                SqlTypedInsertMovementProtocol.BeforeSql, SqlTypedInsertMovementProtocol.InitialEpoch);
            var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, request, token);
            await SqlTypedInsertMovementPages.ReceiptAsync(request, receipt, seed.OriginalPlacement, SqlTypedInsertMovementProtocol.One);
            await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, request, token));
            var failed = request with
            {
                CommandId = SqlTypedInsertMovementProtocol.FailedId
            };
            await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, failed, ErrorCode.Conflict, token);
            await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, failed, ErrorCode.Conflict, dispatched: true, token);
            return new SqlTypedInsertMovementState(identity, request, receipt, failed, seed.OriginalPlacement);
        }, token);
    }
}
