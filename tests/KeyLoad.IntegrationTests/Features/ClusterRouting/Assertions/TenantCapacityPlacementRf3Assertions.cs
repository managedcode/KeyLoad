using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Assertions
{
    internal static async Task RefusalAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(result.Problem.StatusCode).IsEqualTo(Errors.Status(code));
    }

    internal static async Task RequireOriginalModelsAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        await seed.VerifyAsync(token);
        await SqlRf3Protocol.EqualAsync(state.ControlReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(state.ControlCommand, token)));
        await SqlRf3Protocol.EqualAsync(state.ControlReceipt,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, state.ControlCommand, token))).Value);
    }

    internal static async Task RequirePolicyReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.OriginalPolicy.Policy,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigurePlacementPolicyAsync(
                state.OriginalPolicyCommand, state.OriginalPolicy, token)));
        await SqlRf3Protocol.EqualAsync(state.OriginalPolicy.Policy,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
                await seed.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                    new { commandId = state.OriginalPolicyCommand, request = state.OriginalPolicy }, token))).Value);
        await RequireOriginalModelsAsync(seed, state, token);
    }

    internal static async Task RequirePlanAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPlanResultV1 result, CancellationToken token)
    {
        await Assert.That(result.Plan.Version).IsEqualTo(1);
        await Assert.That(result.Plan.Moves.Length).IsEqualTo(1);
        await Assert.That(result.Plan.ControlPartition).IsEqualTo(state.Control);
        var move = result.Plan.Moves.Single();
        await Assert.That(move.Partition).IsEqualTo(seed.Partition);
        await Assert.That(move.MoveId).IsNotEqualTo(Guid.Empty);
        await Assert.That(move.DestinationPhysicalShardId).IsEqualTo(seed.FirstRequest.DestinationPhysicalShardId);
        await Assert.That(move.ExpectedPlacementRevision).IsEqualTo(seed.OriginalPlacement.Revision);
        await Assert.That(result.Plan.DirectoryRevision).IsEqualTo(1);
        await Assert.That(result.Plan.DirectoryRevision).IsGreaterThan(move.ExpectedPlacementRevision);
        await Assert.That(move.Mode).IsEqualTo(PartitionMoveMode.Transfer);
        await Assert.That(result.Plan.CapacityDigests.Length).IsEqualTo(6);
        await Assert.That(result.Plan.ExpiresAt).IsGreaterThan(result.Plan.SampledAt);
        await RequireOriginalModelsAsync(seed, state, token);
    }

    internal static async Task RequirePendingAsync(PlacementIntentV1 intent, TenantPlacementPlanV1 plan, Guid id)
    {
        await Assert.That(intent.IntentId).IsEqualTo(id);
        await Assert.That(intent.Revision).IsEqualTo(1L);
        await Assert.That(intent.Status).IsEqualTo(PlacementIntentStatus.Pending);
        await Assert.That(intent.CompletedMoves).IsEqualTo(0);
        await Assert.That(intent.Receipts.Length).IsEqualTo(0);
        await Assert.That(intent.Claim).IsNull();
        await Assert.That(intent.OriginalApplyCommandId).IsNull();
        await Assert.That(intent.OriginalApplyRequest).IsNull();
        await SqlRf3Protocol.EqualAsync(plan, intent.Plan);
    }
}
