using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal static class DropLifecycleClusterCalls
{
    internal const string Tool = "keyload_tables_drop";
    private const string SqlPrefix = "DROP TABLE [";
    private const string CloseIdentifier = "]";
    private const string EscapedCloseIdentifier = "]]";

    internal static async Task<DropTableResult> InvokeAsync(RequestCqrsClusterCallers caller, Guid parentId,
        DropTableRequest request, PartitionRef partition, DropLifecycleClusterRoute route, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(parentId).IsNotEqualTo(Guid.Empty);
        await Assert.That(partition.TenantId).IsEqualTo(request.TenantId);
        await Assert.That(partition.DatabaseId).IsEqualTo(request.DatabaseId);
        return route switch
        {
            DropLifecycleClusterRoute.Sdk => await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.DropTableAsync(parentId, request, token)),
            DropLifecycleClusterRoute.Mcp => (await McpCallerAssertions.SuccessAsync<DropTableResult>(
                await caller.Mcp.Client.InvokeKeyLoadToolAsync(Tool, Arguments(parentId, request), token))).Value,
            DropLifecycleClusterRoute.SqlSdk => await SqlClusterProtocol.SdkAsync<DropTableResult>(caller.Sdk,
                Sql(parentId, request, partition), token),
            DropLifecycleClusterRoute.SqlMcp => await SqlClusterProtocol.McpAsync<DropTableResult>(caller.Mcp,
                Sql(parentId, request, partition), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }

    internal static Dictionary<string, object?> Arguments(Guid parentId, DropTableRequest request)
    {
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, parentId);
        return arguments;
    }

    internal static SqlOperationRequest Sql(Guid parentId, DropTableRequest request, PartitionRef partition)
        => new(partition, SqlPrefix + request.ResourceName.Replace(CloseIdentifier, EscapedCloseIdentifier,
            StringComparison.Ordinal) + CloseIdentifier, CommandId: parentId);
}
