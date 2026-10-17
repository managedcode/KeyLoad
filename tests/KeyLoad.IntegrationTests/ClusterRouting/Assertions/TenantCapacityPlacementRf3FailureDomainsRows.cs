using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3FailureDomainsRows
{
    internal sealed record Rows(string Intent, string Persist);
    internal static async Task<Rows> RequireAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        TenantCapacityPlacementRf3FailureDomainsOperation.Original original, PlacementIntentV1 applying,
        TenantPlacementPolicyV1 policy, string? retainedPersist)
    {
        var intentKey = TenantPlacementPolicyStorage.IntentKey(policy.TenantId, policy.DatabaseId, applying.IntentId);
        var persistKey = KeySpace.PartitionOutcome(original.Pending.Plan.ControlPartition,
            original.Pending.PrincipalId, original.PersistId);
        var policyKey = TenantPlacementPolicyStorage.PolicyKey(policy.TenantId, policy.DatabaseId);
        var oldMove = original.Pending.Plan.Moves.Single();
        var controls = cuts.Where(cut => Row(cut, intentKey) is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        string? retainedIntent = null;
        foreach (var cut in cuts)
        {
            await Assert.That(Row(cut, PartitionMoveParentKeys.Header(oldMove.Partition, oldMove.MoveId))).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(0L);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(0L);
            if (!controls.Contains(cut))
            {
                await Assert.That(Row(cut, persistKey)).IsNull();
            }
        }
        foreach (var cut in controls)
        {
            var row = Row(cut, intentKey) ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
            retainedIntent ??= row;
            await Assert.That(row).IsEqualTo(retainedIntent);
            await SqlRf3Protocol.EqualAsync(applying, Decode<PlacementIntentV1>(row));
            await SqlRf3Protocol.EqualAsync(policy, Decode<TenantPlacementPolicyV1>(Row(cut, policyKey)!));
            var persist = Row(cut, persistKey) ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
            retainedPersist ??= persist;
            await Assert.That(persist).IsEqualTo(retainedPersist);
            var outcome = Decode<StoredOutcome>(persist);
            await Assert.That(outcome.PolicyEpoch).IsEqualTo(original.Pending.PolicyEpoch);
            await Assert.That(outcome.Incarnation).IsEqualTo(original.Pending.ControlOwner.Incarnation);
            await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await Assert.That(outcome.Partition).IsEqualTo(original.Pending.Plan.ControlPartition);
            await SqlRf3Protocol.EqualAsync(new OperationResult(null), outcome.Result);
            await SqlRf3Protocol.EqualAsync(original.Pending, outcome.Result.Get<PlacementIntentV1>());
        }
        return new(retainedIntent!, retainedPersist!);
    }
    private static T Decode<T>(string row) where T : class
        => NativeSerialization.Deserialize<T>(Convert.FromHexString(row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..]));
    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
        => cut.Rows.SingleOrDefault(row => row.StartsWith(Convert.ToHexString(key) + ":", StringComparison.Ordinal));
}
