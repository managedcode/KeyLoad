using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyWeightsRows
{
    internal sealed record Rows(string Policy, string[] Refusals);
    internal static async Task<Rows> RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        IReadOnlyList<TenantCapacityPlacementRf3PolicyWeightsOperation.Refused> original,
        PartitionMovementPublicParentRf3NativeCut[] cuts, Rows? retained)
    {
        var policyKey = TenantPlacementPolicyStorage.PolicyKey(policy.TenantId, policy.DatabaseId);
        var controls = cuts.Where(cut => Row(cut, policyKey) is not null).ToArray();
        await Assert.That(cuts.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        var rows = new List<string>();
        string? policyRow = null;
        foreach (var cut in controls)
        {
            var row = Row(cut, policyKey) ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
            policyRow ??= row;
            await Assert.That(row).IsEqualTo(policyRow);
            await SqlRf3Protocol.EqualAsync(policy, Decode<TenantPlacementPolicyV1>(row));
        }
        if (retained is not null && Decode<TenantPlacementPolicyV1>(retained.Policy).Revision == policy.Revision)
        {
            await Assert.That(policyRow).IsEqualTo(retained.Policy);
        }
        foreach (var originalFailure in original)
        {
            var key = KeySpace.PartitionOutcome(state.Control,
                PartitionMovementPublicParentRf3Administrator.PrincipalId, originalFailure.Id);
            var expected = retained?.Refusals[rows.Count];
            foreach (var cut in cuts)
            {
                await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(0L);
                await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(0L);
                if (!controls.Contains(cut))
                {
                    await Assert.That(Row(cut, key)).IsNull();
                }
            }
            foreach (var cut in controls)
            {
                var row = Row(cut, key) ?? throw new InvalidOperationException(TenantPlacementPolicyProtocol.Malformed);
                expected ??= row;
                await Assert.That(row).IsEqualTo(expected);
                var outcome = Decode<StoredOutcome>(row);
                await Assert.That(outcome.PolicyEpoch).IsEqualTo(1L);
                await Assert.That(outcome.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
                await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
                await Assert.That(outcome.Partition).IsEqualTo(state.Control);
                await SqlRf3Protocol.EqualAsync(new OperationResult(null,
                    ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid), outcome.Result);
            }
            rows.Add(expected!);
        }
        return new(policyRow!, rows.ToArray());
    }
    private static T Decode<T>(string row) where T : class
        => NativeSerialization.Deserialize<T>(Convert.FromHexString(row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..]));
    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
        => cut.Rows.SingleOrDefault(row => row.StartsWith(Convert.ToHexString(key) + ":", StringComparison.Ordinal));
}
