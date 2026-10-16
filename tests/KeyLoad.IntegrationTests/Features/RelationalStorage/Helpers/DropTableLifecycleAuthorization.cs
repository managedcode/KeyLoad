using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleAuthorization
{
    private const string PrincipalPrefix = "d7-denied-";
    private const string KeyPrefix = "d7-key-";
    private const int SecretBytes = 32;

    internal static Task DeniedAsync(RequestCqrsRf3Callers denied, PartitionRef partition, string table,
        EntityRef row, CommandRequest attemptedWrite, Func<Task> unchanged, CancellationToken token)
        => DropTableLifecycleDenials.DeniedAsync(denied, partition, table, row, attemptedWrite, unchanged, token);

    internal static async Task<string> PersistAsync(TwoRf3MembershipWave wave, PartitionRef partition,
        CancellationToken token)
    {
        var principalId = PrincipalPrefix + Guid.NewGuid().ToString("N");
        var keyId = KeyPrefix + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var principal = new PrincipalRecord(principalId, partition.TenantId, [], []);
        var credential = new ApiKeyRecord(keyId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey,
                async root =>
                {
                    var id = Guid.NewGuid();
                    await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                        await root.Sdk.ConfigurePrincipalAsync(id, principal, token)));
                    var principalArguments = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(principal));
                    principalArguments.Add(McpCallerProtocol.CommandId, id);
                    await SqlRf3Protocol.EqualAsync(principal, (await McpCallerAssertions.SuccessAsync<PrincipalRecord>(
                        await root.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, principalArguments, token))).Value);
                    var keyCommand = Guid.NewGuid();
                    await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(keyCommand, credential, token));
                    var keyArguments = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(credential));
                    keyArguments.Add(McpCallerProtocol.CommandId, keyCommand);
                    await McpCallerAssertions.StatusAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
                        McpCallerTools.CredentialsConfigure, keyArguments, token));
                    return true;
                }, token);
        }
        return secret;
    }
}
