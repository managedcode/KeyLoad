using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClusterSeed
{
    private const int SecretBytes = 32;

    internal static async Task<(McpPersistedIdentity Writer, McpPersistedIdentity Reader)> ConfigureAsync(
        KeyLoadClient root, CancellationToken token)
    {
        var partition = PostgresClusterState.Partition;
        await RelationalSqlClusterJoinBudgetData.ConfigureAsync(root, partition, token);
        var writer = await IdentityAsync(root, WriterMarker,
            Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token);
        var reader = await IdentityAsync(root, ReaderMarker, Capability.DocumentsRead | Capability.Query, token);
        return (writer, reader);
    }

    internal static async Task<McpPersistedIdentity> IdentityAsync(KeyLoadClient root, string marker,
        Capability capabilities, CancellationToken token, string? singleResource = null)
    {
        var partition = PostgresClusterState.Partition;
        var principal = new PrincipalRecord(PostgresClusterProtocol.PrincipalPrefix + marker
            + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat), partition.TenantId,
            singleResource is null
                ? [new(partition.DatabaseId, RelationalSqlClusterJoinBudgetData.LeftCollection, capabilities),
                   new(partition.DatabaseId, RelationalSqlClusterJoinBudgetData.RightCollection, capabilities)]
                : [new(partition.DatabaseId, singleResource, capabilities)], []);
        var id = PostgresClusterProtocol.CredentialPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = id + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(id, principal.Id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        var persisted = await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlClusterProtocol.EqualAsync(principal, persisted);
        await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token));
        return new(persisted, credential, secret);
    }

    internal static CommandRequest Command()
        => new(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(RelationalSqlClusterJoinBudgetData.LeftCollection, PostgresClusterState.OrderId, PostgresClusterState.LeftJson),
             new PutDocument(RelationalSqlClusterJoinBudgetData.RightCollection, PostgresClusterState.CustomerId, PostgresClusterState.RightJson)]);

    private const string WriterMarker = "writer-";
    private const string ReaderMarker = "reader-";
}
