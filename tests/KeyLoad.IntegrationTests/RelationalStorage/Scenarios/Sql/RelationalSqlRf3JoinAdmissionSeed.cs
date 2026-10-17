using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class RelationalSqlRf3JoinAdmissionSeed
{
    internal const int Dialect = 2;
    internal const int Limit = 10;
    private const int SecretBytes = 32;
    internal const long Revision = 1;
    internal const string Right = "joincustomers";
    internal const string Order = "cancelled-join-order";
    internal const string Customer = "cancelled-join-customer";
    internal const string LeftJson = "{\"key\":\"cancelled-join-order\",\"title\":\"cancelled-join-customer\",\"count\":1}";
    internal const string RightJson = "{\"id\":\"cancelled-join-customer\",\"name\":\"Ada\"}";
    internal const string SqlText = "SELECT TOP(10) l.[key] AS order_key, r.name AS customer_name, l.title AS customer_id "
                                    + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id ORDER BY l.[key] ASC";

    internal static async Task<RelationalQ2AdmissionSeed> CreateAsync(KeyLoadClient admin, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(admin, token);
        var right = new ResourceDefinition(Right, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RelationalSqlRf3JoinBudgetData.CustomerIdColumn,
            [new(RelationalSqlRf3JoinBudgetData.CustomerIdColumn, RelationalColumnType.Text),
                new("name", RelationalColumnType.Text)])
        };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(admin,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right),
                Guid.NewGuid()), token);
        await SqlRf3Protocol.EqualAsync(right, configured);
        var identity = await CreateIdentityAsync(admin, scenario.Partition, token);
        var command = scenario.Command(new PutDocument(RelationalSqlRf3Tokens.Table, Order, LeftJson),
            new PutDocument(Right, Customer, RightJson));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        return new(scenario, identity, command, receipt);
    }

    private static async Task<McpPersistedIdentity> CreateIdentityAsync(KeyLoadClient admin,
        PartitionRef partition, CancellationToken token)
    {
        var id = "c1-probe-q2-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var read = Capability.Query | Capability.DocumentsRead;
        var principal = new PrincipalRecord(id, partition.TenantId,
        [new(partition.DatabaseId, RelationalSqlRf3Tokens.Table, read),
            new(partition.DatabaseId, Right, read)], []);
        var keyId = id + "-credential";
        var secret = keyId + "." + Convert.ToHexStringLower(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(SecretBytes));
        var hash = global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(secret));
        var credential = new ApiKeyRecord(keyId, id, Convert.ToHexStringLower(hash));
        var saved = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlRf3Protocol.EqualAsync(principal, saved);
        await Assert.That((await McpCallerAssertions.SdkStatusAsync(
            await admin.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsSuccess).IsTrue();
        return new(saved, credential, secret);
    }
}
