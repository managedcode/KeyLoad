using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCursorRequestIdentityR47Callers
{
    internal static Task<T> RunAsync<T>(ClusterFixture fixture, string credential,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task<T>> action, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                credential, reader => action(root, reader), token), token);
}
