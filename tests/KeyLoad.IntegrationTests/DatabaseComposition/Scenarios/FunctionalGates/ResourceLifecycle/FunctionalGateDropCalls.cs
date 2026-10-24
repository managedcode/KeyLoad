using KeyLoad.Client;
using KeyLoad.IntegrationTests.RelationalStorage;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateDropCalls
{
    internal static Task<DropTableResult> InvokeAsync(FunctionalGateContext context, Guid parentId,
        DropTableRequest request, DropLifecycleClusterRoute route, CancellationToken token)
        => route switch
        {
            DropLifecycleClusterRoute.Sdk => context.SdkAsync(
                context.Administrator.Sdk.DropTableAsync(parentId, request, token)),
            DropLifecycleClusterRoute.Mcp => context.McpHeaderAsync<DropTableResult>(context.Administrator,
                DropLifecycleClusterCalls.Tool, request, parentId, token),
            DropLifecycleClusterRoute.SqlSdk => FunctionalGateRelationalCalls.SqlSdkAsync<DropTableResult>(
                context, context.Administrator, DropLifecycleClusterCalls.Sql(parentId, request, context.Partition), token),
            DropLifecycleClusterRoute.SqlMcp => FunctionalGateRelationalCalls.SqlMcpAsync<DropTableResult>(
                context, context.Administrator, DropLifecycleClusterCalls.Sql(parentId, request, context.Partition), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
}
