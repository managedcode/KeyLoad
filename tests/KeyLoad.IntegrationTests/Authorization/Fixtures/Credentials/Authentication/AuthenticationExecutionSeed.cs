using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.RecoveryTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class AuthenticationExecutionSeed
{
    internal static async Task<AuthenticationExecutionState> CreateAsync(
        RequestCqrsHostedRuntimeFixture host, CancellationToken token)
    {
        using var http = new HttpClient { BaseAddress = host.PublicEndpoint, Timeout = Timeout.InfiniteTimeSpan };
        var root = new KeyLoadClient(http, host.AdminKey, IntegrationClientOptions.Execution());
        var partition = new PartitionRef(McpDocumentProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            McpDocumentProtocol.Database, McpDocumentProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var creationId = Guid.NewGuid();
        var definition = new ResourceDefinition(McpDocumentProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var document = McpDocumentScenario.ForCreation(partition, creationId, definition);
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(creationId,
            new(partition.TenantId, partition.DatabaseId, definition), token));
        var command = document.Command(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(command, token));
        var reader = new PrincipalRecord(AuthenticationExecutionProtocol.PrincipalPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            partition.TenantId, [new(partition.DatabaseId, McpDocumentProtocol.Collection, Capability.DocumentsRead)], []);
        var id = AuthenticationExecutionProtocol.CredentialPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var secret = id + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var credential = new ApiKeyRecord(id, reader.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), reader, token));
        await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token));
        return new(document, reader, credential, secret, command, receipt);
    }
}
