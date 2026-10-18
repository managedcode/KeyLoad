using KeyLoad.Client;
using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionChanges
{
    internal static async Task WarmAsync(ControlledPartitionMovementNativeHost host,
        AuthenticationExecutionState state, AuthenticationExecutionCollector collector, CancellationToken token)
    {
        using var rootHttp = new HttpClient { BaseAddress = new(host.Runtime.Node.Value.PublicEndpoint), Timeout = Timeout.InfiniteTimeSpan };
        var root = new KeyLoadClient(rootHttp, host.Runtime.Node.Value.AdminKey, IntegrationClientOptions.Execution());
        await AuthenticationExecutionClients.RunAsync(host, state, collector, async (reader, mcp, http) =>
        {
            await AuthenticationExecutionValues.HealthyAsync(reader, mcp, http, collector, state, state.Expected, token);
            await AuthenticationExecutionWriteRefusal.RunAsync(root, reader, mcp, http, collector, state, token);
            await PersistAsync(root, state.Reader with
            {
                Grants = [],
                PolicyEpoch = AuthenticationExecutionProtocol.SecondEpoch
            }, token);
            await AuthenticationExecutionRefusals.PermissionAsync(root, reader, mcp, http, collector, state, token);
            await PersistAsync(root, state.Reader with
            {
                PolicyEpoch = AuthenticationExecutionProtocol.RepairedEpoch
            }, token);
            await AuthenticationExecutionResourcePolicy.RunAsync(root, reader, mcp, http, collector, state, token);
            await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential with
            {
                Revoked = true
            }, token));
            await AuthenticationExecutionRefusals.UnauthenticatedAsync(root, reader, mcp, http, collector, state, token);
            await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential with
            {
                ExpiresAt = host.Clock.GetUtcNow() - McpCallerProtocol.ExpiredOffset
            }, token));
            await AuthenticationExecutionRefusals.UnauthenticatedAsync(root, reader, mcp, http, collector, state, token);
            await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential, token));
            await PersistAsync(root, state.Reader with
            {
                Revoked = true,
                PolicyEpoch = AuthenticationExecutionProtocol.RepairedEpoch + AuthenticationExecutionProtocol.One
            }, token);
            await AuthenticationExecutionRefusals.UnauthenticatedAsync(root, reader, mcp, http, collector, state, token);
            await PersistAsync(root, state.Reader with
            {
                PolicyEpoch = AuthenticationExecutionProtocol.FinalEpoch
            }, token);
            await AuthenticationExecutionValues.HealthyAsync(reader, mcp, http, collector, state, state.Expected, token);
        }, token);
        await AuthenticationExecutionValues.ReplayAsync(root, state, token);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
