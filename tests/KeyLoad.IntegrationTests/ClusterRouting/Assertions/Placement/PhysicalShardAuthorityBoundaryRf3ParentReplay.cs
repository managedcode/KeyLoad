using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3ParentReplay
{
    private const long RevisionStep = 1;
    internal static async Task RequireAsync(PhysicalShardAuthorityBoundaryRf3Actor actor,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest original,
        PartitionMoveResult expected, CancellationToken token)
    {
        await HealthyAsync(actor, seed, original, expected, token);
        var changed = original with
        {
            ExpectedPlacementRevision = checked(original.ExpectedPlacementRevision + RevisionStep)
        };
        await ConflictAsync(actor, seed, changed, token);
        await HealthyAsync(actor, seed, original with
        {
            Mode = PartitionMoveMode.Resume
        }, expected, token);
    }

    private static async Task HealthyAsync(PhysicalShardAuthorityBoundaryRf3Actor actor,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request,
        PartitionMoveResult expected, CancellationToken token)
    {
        var sdk = await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(actor.Sdk.MovePartitionAsync(request, token));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(sdk));
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        var mcp = await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            actor.Mcp.CallAsync(PartitionMovePublicProtocol.ToolName, request, token));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(mcp)).Value);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        var sql = SqlRf3Protocol.Call(request.Partition, PartitionMovePublicProtocol.ToolName, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<PartitionMoveResult>(actor.Sdk, sql, token));
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<PartitionMoveResult>(actor.Mcp, sql, token));
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
    }

    private static async Task ConflictAsync(PhysicalShardAuthorityBoundaryRf3Actor actor,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request, CancellationToken token)
    {
        var sdk = await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(actor.Sdk.MovePartitionAsync(request, token));
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(sdk.Problem, ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.MovementConflictDetail);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            actor.Mcp.CallAsync(PartitionMovePublicProtocol.ToolName, request, token)), ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        var sql = SqlRf3Protocol.Call(request.Partition, PartitionMovePublicProtocol.ToolName, request);
        var sqlResult = await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(actor.Sdk.ExecuteSqlAsync(sql, token));
        await Assert.That(sqlResult.IsSuccess).IsFalse();
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(sqlResult.Problem, ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.MovementConflictDetail);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await PhysicalShardAuthorityBoundaryRf3Flow.ObserveOriginalAsync(
            actor.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token)), ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
    }
}
