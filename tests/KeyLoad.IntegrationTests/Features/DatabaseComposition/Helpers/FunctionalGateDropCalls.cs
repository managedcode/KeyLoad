using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateDropCalls
{
    internal static Task<DropTableResult> InvokeAsync(FunctionalGateContext context, Guid parentId,
        DropTableRequest request, DropLifecycleRf3Route route, CancellationToken token)
        => route switch
        {
            DropLifecycleRf3Route.Sdk => context.SdkAsync(
                context.Administrator.Sdk.DropTableAsync(parentId, request, token)),
            DropLifecycleRf3Route.Mcp => context.McpHeaderAsync<DropTableResult>(context.Administrator,
                DropLifecycleRf3Calls.Tool, request, parentId, token),
            DropLifecycleRf3Route.SqlSdk => FunctionalGateRelationalCalls.SqlSdkAsync<DropTableResult>(
                context, context.Administrator, DropLifecycleRf3Calls.Sql(parentId, request, context.Partition), token),
            DropLifecycleRf3Route.SqlMcp => FunctionalGateRelationalCalls.SqlMcpAsync<DropTableResult>(
                context, context.Administrator, DropLifecycleRf3Calls.Sql(parentId, request, context.Partition), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
}
