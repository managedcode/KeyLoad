using KeyLoad.Core;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using P = KeyLoad.IntegrationTests.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.OrleansExecution;

internal static class CatalogBootstrapAuthorityChange
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers survivor,
        string variant, CancellationToken token)
    {
        var command = Guid.NewGuid();
        if (variant == P.KeyRevoked)
        {
            var credential = AuthorizationExecution.Credential(P.Root, P.Root, fixture.AdminKey) with
            {
                Revoked = true
            };
            await McpCallerAssertions.SdkStatusAsync(await survivor.Sdk.ConfigureApiKeyAsync(command, credential, token));
            var args = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(credential));
            args.Add(McpCallerProtocol.CommandId, command);
            await McpCallerAssertions.StatusAsync(await survivor.Mcp.Client.InvokeKeyLoadToolAsync(
                McpCallerTools.CredentialsConfigure, args, token));
            return;
        }
        PrincipalRecord principal = new(P.Root, P.SystemTenant,
            variant == P.PrincipalRevoked ? [new(P.Wildcard, P.Wildcard, Capability.All)] : [],
            variant == P.PrincipalRevoked ? [P.Wildcard] : [])
        {
            ClusterAdministrator = variant == P.PrincipalRevoked,
            Revoked = variant == P.PrincipalRevoked,
            PolicyEpoch = P.ChangedEpoch
        };
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await survivor.Sdk.ConfigurePrincipalAsync(command, principal, token)));
        var principalArgs = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(principal));
        principalArgs.Add(McpCallerProtocol.CommandId, command);
        await SqlClusterProtocol.EqualAsync(principal, (await McpCallerAssertions.SuccessAsync<PrincipalRecord>(
            await survivor.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, principalArgs, token))).Value);
    }
}
