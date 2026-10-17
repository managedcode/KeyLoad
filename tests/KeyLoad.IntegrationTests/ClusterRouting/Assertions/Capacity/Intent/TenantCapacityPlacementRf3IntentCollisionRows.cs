using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionRows
{
    private const char Separator = ':';
    internal static async Task<TenantCapacityPlacementRf3IntentCollisionRowsState> RequireAsync(
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantCapacityPlacementRf3IntentCollisionState original, PartitionMovementPublicParentRf3NativeCut[] cuts,
        TenantCapacityPlacementRf3IntentCollisionRowsState? retained)
    {
        await Assert.That(cuts.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        var policyKey = TenantPlacementPolicyStorage.PolicyKey(seed.Partition.TenantId, seed.Partition.DatabaseId);
        var intentKey = TenantPlacementPolicyStorage.IntentKey(seed.Partition.TenantId, seed.Partition.DatabaseId, original.Request.IntentId);
        var successKey = Outcome(original.PersistId, capacity.Control);
        var failureKey = Outcome(original.CollisionId, capacity.Control);
        var controls = cuts.Where(cut => Row(cut, policyKey) is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in cuts)
        {
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(0L);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(0L);
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(Row(cut, PartitionMoveParentKeys.Header(original.Move.Partition, original.Move.MoveId))).IsNull();
            await Assert.That(Row(cut, PartitionMoveParentKeys.Header(original.CollisionMove.Partition, original.CollisionMove.MoveId))).IsNull();
            if (!controls.Contains(cut))
            {
                await Assert.That(Row(cut, intentKey)).IsNull();
                await Assert.That(Row(cut, successKey)).IsNull();
                if (original.CollisionId != Guid.Empty)
                {
                    await Assert.That(Row(cut, failureKey)).IsNull();
                }
                continue;
            }
            var intent = Row(cut, intentKey);
            var success = Row(cut, successKey);
            await Assert.That(intent).IsNotNull();
            await Assert.That(success).IsNotNull();
            var failure = original.CollisionId == Guid.Empty ? null : Row(cut, failureKey);
            if (original.CollisionId != Guid.Empty)
            {
                await Assert.That(failure).IsNotNull();
            }
            retained ??= new(intent!, success!, failure);
            await Assert.That(intent).IsEqualTo(retained.Intent);
            await Assert.That(success).IsEqualTo(retained.Success);
            if (retained.Failure is null && failure is not null)
            {
                retained = retained with
                {
                    Failure = failure
                };
            }
            await Assert.That(failure).IsEqualTo(retained.Failure);
            await SqlRf3Protocol.EqualAsync(original.Pending, Decode<PlacementIntentV1>(intent!));
            await TenantCapacityPlacementRf3IntentCollisionOutcome.RequireAsync(seed, capacity, Decode<StoredOutcome>(success!), false);
            if (failure is not null)
            {
                await TenantCapacityPlacementRf3IntentCollisionOutcome.RequireAsync(seed, capacity, Decode<StoredOutcome>(failure), true);
            }
            await SqlRf3Protocol.EqualAsync(capacity.HealthyPolicy, Decode<TenantPlacementPolicyV1>(Row(cut, policyKey)!));
        }
        return retained ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
    }
    internal static T Decode<T>(string row) where T : class
        => NativeSerialization.Deserialize<T>(Convert.FromHexString(row[(row.IndexOf(Separator, StringComparison.Ordinal) + 1)..]));
    private static byte[] Outcome(Guid id, PartitionRef control)
        => CommandOutcomeKeyResolver.ForNew(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            id, new(CommandOutcomeScopeKind.Partition, control)).Key;
    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
        => cut.Rows.SingleOrDefault(row => row.StartsWith(Convert.ToHexString(key) + Separator, StringComparison.Ordinal));
}
