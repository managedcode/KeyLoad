using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Metadata
{
    internal static async Task<ResourceDefinition> ConfigureAsync(RequestCqrsRf3Callers callers,
        ResourcePartitionInventoryRf3Creation creation, CancellationToken token)
    {
        var arguments = McpOfficialClient.Arguments(creation.Request);
        arguments.Add(McpCallerProtocol.CommandId, creation.CommandId);
        return (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(await callers.Mcp.Client
            .InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, arguments, token).ConfigureAwait(false))).Value;
    }

    internal static async Task<PrincipalRecord> PrincipalAsync(RequestCqrsRf3Callers callers,
        PrincipalRecord principal, CancellationToken token)
    {
        var arguments = McpOfficialClient.Arguments(new ConfigurePrincipalRequest(principal));
        arguments.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
        return (await McpCallerAssertions.SuccessAsync<PrincipalRecord>(await callers.Mcp.Client
            .InvokeKeyLoadToolAsync(McpCallerTools.PrincipalsConfigure, arguments, token).ConfigureAwait(false))).Value;
    }
}
