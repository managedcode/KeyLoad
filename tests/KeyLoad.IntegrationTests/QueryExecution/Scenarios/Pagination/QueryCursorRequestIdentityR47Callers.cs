using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCursorRequestIdentityR47Callers
{
    internal static Task<T> RunAsync<T>(ClusterFixture fixture, string credential,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task<T>> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                credential, reader => action(root, reader), token), token);
}
