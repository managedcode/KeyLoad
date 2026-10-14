using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3DemotionAssertions
{
    private const string McpInvalidDetail = "The database operation could not be completed.";
    private const int FormatVersion = 1;
    private const int CompleteCapacityMembers = 6;
    private const int SingleMove = 1;
    private const int NoMoves = 0;
    private const long PromotionPublication = 2;

    internal static async Task PlanAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        TenantPlacementPlanResultV1 result, bool moveExpected, CancellationToken token)
    {
        var plan = result.Plan;
        await Assert.That(plan.Version).IsEqualTo(FormatVersion);
        await Assert.That(plan.TenantId).IsEqualTo(seed.Partition.TenantId);
        await Assert.That(plan.DatabaseId).IsEqualTo(seed.Partition.DatabaseId);
        await Assert.That(plan.PolicyRevision).IsEqualTo(policy.Revision);
        await Assert.That(plan.DirectoryRevision).IsEqualTo(PromotionPublication);
        await Assert.That(plan.ControlPartition).IsEqualTo(state.Control);
        await SqlRf3Protocol.EqualAsync(new[] { seed.Partition }, plan.OriginalPartitions.ToArray());
        await Assert.That(plan.CapacityDigests.Length).IsEqualTo(CompleteCapacityMembers);
        await Assert.That(plan.Moves.Length).IsEqualTo(moveExpected ? SingleMove : NoMoves);
        await Assert.That(plan.PlanId).IsNotEqualTo(Guid.Empty);
        await Assert.That(plan.ExpiresAt).IsGreaterThan(plan.SampledAt);
        if (moveExpected)
        {
            var move = plan.Moves.Single();
            await Assert.That(move.MoveId).IsNotEqualTo(Guid.Empty);
            await Assert.That(move.Partition).IsEqualTo(seed.Partition);
            await Assert.That(move.DestinationPhysicalShardId).IsEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
            await Assert.That(move.ExpectedPlacementRevision).IsEqualTo(PromotionPublication);
            await Assert.That(move.Mode).IsEqualTo(PartitionMoveMode.Transfer);
        }
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }

    internal static async Task EmptyRefusalAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, PersistPlacementIntentRequest request, CancellationToken token)
    {
        var sdk = await seed.Source.PersistPlacementIntentAsync(id, request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Validation);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Validation,
            TenantPlacementPolicyProtocol.Invalid), sdk.Problem!);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = id,
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Validation, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpInvalidDetail);
    }
}
