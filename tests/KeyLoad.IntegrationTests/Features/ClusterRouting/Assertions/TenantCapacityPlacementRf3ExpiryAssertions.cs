using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ExpiryAssertions
{
    private const string McpExpiredDetail = "The database operation could not be completed.";
    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, PersistPlacementIntentRequest request, CancellationToken token)
    {
        var original = await seed.Source.PersistPlacementIntentAsync(id, request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(original, ErrorCode.TokenInvalidated);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.TokenInvalidated,
            TenantPlacementPolicyProtocol.ExpiredPlan), original.Problem!);
        var official = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = id,
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.TokenInvalidated, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(McpExpiredDetail);
    }

    internal static async Task ColdAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMovementPublicParentRf3NativeCut[] baseline, Guid commandId, Guid intentId,
        CancellationToken token)
    {
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await RequireImagesAsync(state, baseline, stopped, commandId, intentId);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, seed.FirstRequest, stopped);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(stopped);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var second = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await RequireImagesAsync(state, stopped, second, commandId, intentId);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, seed.FirstRequest, second);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(second);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }

    internal static async Task PersistReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        Guid id, PersistPlacementIntentRequest request, PlacementIntentV1 pending, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(pending,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PersistPlacementIntentAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(pending,
            (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.PersistTool, new
                {
                    commandId = id,
                    request
                }, token))).Value);
        await TenantCapacityPlacementRf3MultiContinuation.RequireAsync(seed, token);
    }

    private static async Task RequireImagesAsync(TenantCapacityPlacementRf3Provisioning.State state,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after,
        Guid commandId, Guid intentId)
    {
        await Assert.That(after.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        var intent = TenantPlacementPolicyStorage.IntentKey(state.Control.TenantId, state.Control.DatabaseId, intentId);
        var outcome = CommandOutcomeKeyResolver.ForNew(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            commandId, new(CommandOutcomeScopeKind.Partition, state.Control)).Key;
        foreach (var current in after)
        {
            var original = before.Single(value => value.Node == current.Node);
            await Assert.That(current.Rows.SequenceEqual(original.Rows)).IsTrue();
            await Assert.That(current.Header).IsNull();
            await Assert.That(current.Pending).IsNull();
            await Assert.That(current.LastIssued).IsNull();
            await Assert.That(current.OutstandingMoveGrants).IsEqualTo(original.OutstandingMoveGrants);
            await Assert.That(current.OutstandingDatabaseGrants).IsEqualTo(original.OutstandingDatabaseGrants);
            await Assert.That(current.Rows.Any(row => row.StartsWith(Convert.ToHexString(intent) + ":",
                StringComparison.Ordinal))).IsFalse();
            await Assert.That(current.Rows.Any(row => row.StartsWith(Convert.ToHexString(outcome) + ":",
                StringComparison.Ordinal))).IsFalse();
        }
    }
}
