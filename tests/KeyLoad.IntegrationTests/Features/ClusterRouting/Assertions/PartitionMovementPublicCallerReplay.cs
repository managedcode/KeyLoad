using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Exercises genuine SDK, official MCP and Q1 callers against the retained actual parent terminal authority.</summary>
internal static class PartitionMovementPublicCallerReplay
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionMoveRequest original, PartitionMoveResult expected, CancellationToken cancellationToken)
    {
        await RequireHealthyAsync(sdk, mcp, original, expected, cancellationToken);
        var changed = original with { ExpectedPlacementRevision = checked(original.ExpectedPlacementRevision + 1) };
        var sdkDenied = await sdk.MovePartitionAsync(changed, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdkDenied.IsSuccess).IsFalse();
        await Assert.That(sdkDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(PartitionMovePublicProtocol.ToolName,
            changed, cancellationToken).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true);
        var sql = SqlRf3Protocol.Call(original.Partition, PartitionMovePublicProtocol.ToolName, changed);
        var sqlDenied = await sdk.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false);
        await Assert.That(sqlDenied.IsSuccess).IsFalse();
        await Assert.That(sqlDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql,
            cancellationToken).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true);
        await RequireHealthyAsync(sdk, mcp, original with { Mode = PartitionMoveMode.Resume }, expected,
            cancellationToken);
    }

    internal static async Task RequireDeniedSubjectAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionMoveRequest original, CancellationToken cancellationToken)
    {
        var denied = await sdk.MovePartitionAsync(original, cancellationToken).ConfigureAwait(false);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(PartitionMovePublicProtocol.ToolName,
            original, cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlRf3Protocol.Call(original.Partition, PartitionMovePublicProtocol.ToolName, original);
        var deniedSql = await sdk.ExecuteSqlAsync(sql, cancellationToken).ConfigureAwait(false);
        await Assert.That(deniedSql.IsSuccess).IsFalse();
        await Assert.That(deniedSql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql,
            cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task RequireHealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionMoveRequest request, PartitionMoveResult expected, CancellationToken cancellationToken)
    {
        var actualSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.MovePartitionAsync(request,
            cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, actualSdk);
        var actualMcp = await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(await mcp.CallAsync(
            PartitionMovePublicProtocol.ToolName, request, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, actualMcp.Value);
        var sql = SqlRf3Protocol.Call(request.Partition, PartitionMovePublicProtocol.ToolName, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<PartitionMoveResult>(sdk,
            sql, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<PartitionMoveResult>(mcp,
            sql, cancellationToken).ConfigureAwait(false));
    }
}
