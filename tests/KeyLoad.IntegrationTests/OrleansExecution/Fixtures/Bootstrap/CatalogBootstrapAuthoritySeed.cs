using KeyLoad.Core;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using P = KeyLoad.IntegrationTests.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.OrleansExecution;

internal static class CatalogBootstrapAuthoritySeed
{
    internal static async Task<CatalogBootstrapAuthorityState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(P.GuidFormat);
        PartitionRef partition = new(P.TenantPrefix + suffix, P.Database, P.TenantPrefix + suffix, P.TenantPrefix + suffix);
        PrincipalRecord administrator = new(P.AdministratorPrefix + suffix, P.SystemTenant, [], [])
        {
            ClusterAdministrator = true
        };
        PrincipalRecord reader = new(P.ReaderPrefix + suffix, partition.TenantId,
            [new(P.Database, P.Collection, Capability.DocumentsRead)], []);
        var administratorKey = administrator.Id + P.SecretSeparator + Guid.NewGuid().ToString(P.GuidFormat);
        var readerKey = reader.Id + P.SecretSeparator + Guid.NewGuid().ToString(P.GuidFormat);
        ResourceDefinition resource = new(P.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        await SqlClusterProtocol.EqualAsync(resource, configured);
        await PrincipalAsync(root, administrator, administratorKey, token);
        await PrincipalAsync(root, reader, readerKey, token);
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, P.Nodes[P.FirstIndex], administratorKey, async survivor =>
        {
            var command = new CommandRequest(Guid.NewGuid(), partition,
                [new PutDocument(P.Collection, P.Document, P.OriginalJson, P.EmptyRevision)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await survivor.Sdk.CommitAsync(command, token));
            await CatalogBootstrapAuthorityCurrent.ReceiptAsync(command, receipt, P.OriginalRevision);
            return new CatalogBootstrapAuthorityState(partition, administrator, administratorKey, reader, readerKey, command, receipt);
        }, token);
    }

    private static async Task PrincipalAsync(RequestCqrsClusterCallers root, PrincipalRecord principal,
        string secret, CancellationToken token)
    {
        var command = Guid.NewGuid();
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(command, principal, token)));
        var args = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(principal));
        args.Add(McpCallerProtocol.CommandId, command);
        await SqlClusterProtocol.EqualAsync(principal, (await McpCallerAssertions.SuccessAsync<PrincipalRecord>(
            await root.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, args, token))).Value);
        var credential = AuthorizationExecution.Credential(principal.Id, principal.Id, secret);
        var credentialCommand = Guid.NewGuid();
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(credentialCommand, credential, token));
        var credentialArgs = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(credential));
        credentialArgs.Add(McpCallerProtocol.CommandId, credentialCommand);
        await McpCallerAssertions.StatusAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.CredentialsConfigure, credentialArgs, token));
    }
}
