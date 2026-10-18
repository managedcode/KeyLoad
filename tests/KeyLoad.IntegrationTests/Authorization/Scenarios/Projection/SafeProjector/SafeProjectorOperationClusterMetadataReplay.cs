using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterMetadataReplay
{
    internal static async Task AllAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var state = history.State;
        var resourceRequest = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, state.Resource);
        await ObserveAsync(root, reader, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(state.ResourceId, resourceRequest, token)));
        }, token);
        await OfficialAsync<ResourceDefinition, ConfigureResourceRequest>(root, reader, history,
            McpCallerTools.ResourcesConfigure, state.ResourceId, resourceRequest, state.Resource, token);
        foreach (var command in history.PrincipalCommands)
        {
            await ObserveAsync(root, reader, history, async () =>
            {
                await SqlClusterProtocol.EqualAsync(command.Principal, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.ConfigurePrincipalAsync(command.Id, command.Principal, token)));
            }, token);
            await OfficialAsync<PrincipalRecord, ConfigurePrincipalRequest>(root, reader, history,
                McpCallerTools.PrincipalsConfigure, command.Id, new(command.Principal), command.Principal, token);
        }
        await ObserveAsync(root, reader, history, async () =>
        {
            await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(state.CredentialId, state.Credential, token));
        }, token);
        await ObserveAsync(root, reader, history, async () =>
        {
            var arguments = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(state.Credential));
            arguments.Add(McpCallerProtocol.CommandId, state.CredentialId);
            await McpCallerAssertions.StatusAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.CredentialsConfigure, arguments, token));
        }, token);
    }

    private static async Task OfficialAsync<T, TRequest>(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, SafeProjectorOperationClusterHistory history,
        string tool, Guid id, TRequest request, T expected, CancellationToken token)
    {
        await ObserveAsync(root, reader, history, async () =>
        {
            var arguments = McpOfficialClient.Arguments(request);
            arguments.Add(McpCallerProtocol.CommandId, id);
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(
                await root.Mcp.Client.InvokeKeyLoadToolAsync(tool, arguments, token))).Value);
        }, token);
    }
    private static async Task ObserveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, Func<Task> operation, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(operation, failures);
            await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationClusterCurrentAuthority.VerifyAsync(
                reader, history, token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, token);
    }
}
