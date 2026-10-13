using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevisionRows
{
    private const int OriginalEpoch = 1;
    private const char KeyValueSeparator = ':';
    internal static async Task<string> RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        TenantCapacityPlacementRf3PolicyRevision.Refused original, PartitionMovementPublicParentRf3NativeCut[] cuts,
        string? retained)
    {
        await Assert.That(cuts.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        var policyKey = TenantPlacementPolicyStorage.PolicyKey(policy.TenantId, policy.DatabaseId);
        var intentKey = TenantPlacementPolicyStorage.IntentKey(policy.TenantId, policy.DatabaseId, original.Request.IntentId);
        var outcomeKey = CommandOutcomeKeyResolver.ForNew(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            original.Id, new(CommandOutcomeScopeKind.Partition, state.Control)).Key;
        var controls = cuts.Where(cut => Row(cut, policyKey) is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in cuts)
        {
            await Assert.That(cut.Header).IsNull();
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(0L);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(0L);
            await Assert.That(Row(cut, intentKey)).IsNull();
            if (!controls.Contains(cut))
            { await Assert.That(Row(cut, outcomeKey)).IsNull(); }
        }
        foreach (var cut in controls)
        {
            var policyRow = Row(cut, policyKey)!;
            await SqlRf3Protocol.EqualAsync(policy, Decode<TenantPlacementPolicyV1>(policyRow));
            var row = Row(cut, outcomeKey);
            await Assert.That(row).IsNotNull();
            retained ??= row!;
            await Assert.That(row).IsEqualTo(retained);
            var outcome = Decode<StoredOutcome>(row!);
            await Assert.That(outcome.PolicyEpoch).IsEqualTo(OriginalEpoch);
            await Assert.That(outcome.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
            await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await Assert.That(outcome.Partition).IsEqualTo(state.Control);
            await Assert.That(outcome.Result.NativeValue).IsNull();
            await SqlRf3Protocol.EqualAsync(new OperationResult(null, ErrorCode.Conflict,
                TenantPlacementPolicyProtocol.Stale), outcome.Result);
        }
        return retained ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
    }
    private static T Decode<T>(string row) where T : class
        => NativeSerialization.Deserialize<T>(Convert.FromHexString(row[(row.IndexOf(KeyValueSeparator, StringComparison.Ordinal) + 1)..]));
    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
        => cut.Rows.SingleOrDefault(row => row.StartsWith(Convert.ToHexString(key) + KeyValueSeparator, StringComparison.Ordinal));
}
