using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Callers
{
    internal static Task<T> RunAsync<T>(ClusterFixture fixture, string credential,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task<T>> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                credential, reader => action(root, reader), token), token);
}
