using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedRows
{
    internal static async Task RequireAsync(TenantCapacityPlacementRf3Provisioning.State capacity,
        TenantDedicatedOwnerRf3State packed, PartitionMovementPublicParentRf3NativeCut[] original,
        PartitionMovementPublicParentRf3NativeCut[] current, PartitionMoveRequest retained, CancellationToken token)
    {
        await TenantCapacityPlacementRf3Cold.RequireCanonicalRowsAsync(capacity, original, current, null, packed.DeniedMove);
        var siblingOutcome = CommandOutcomeKeyResolver.ForNew(packed.Sibling.PrincipalId,
            packed.SiblingSeed.CommandId, new(CommandOutcomeScopeKind.Partition, packed.Sibling.Partition)).Key;
        var controlOutcome = CommandOutcomeKeyResolver.ForNew(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            capacity.ControlCommand.CommandId, new(CommandOutcomeScopeKind.Partition, capacity.Control)).Key;
        var forwardHeader = PartitionMoveParentKeys.Header(packed.DeniedMove.Partition, packed.DeniedMove.MoveId);
        var retainedHeader = PartitionMoveParentKeys.Header(retained.Partition, retained.MoveId);
        await Assert.That(original.Any(cut => Row(cut, forwardHeader) is not null)).IsTrue();
        await Assert.That(original.Any(cut => Row(cut, retainedHeader) is not null)).IsTrue();
        foreach (var before in original)
        {
            token.ThrowIfCancellationRequested();
            var after = current.Single(cut => cut.Node == before.Node);
            await SqlRf3Protocol.EqualAsync(before.Header, after.Header);
            await SqlRf3Protocol.EqualAsync(before.LastIssued, after.LastIssued);
            await Assert.That(before.Pending).IsNull();
            await Assert.That(after.Pending).IsNull();
            await Assert.That(Row(after, forwardHeader)).IsEqualTo(Row(before, forwardHeader));
            await Assert.That(Row(after, retainedHeader)).IsEqualTo(Row(before, retainedHeader));
            await Assert.That(Row(after, siblingOutcome)).IsEqualTo(Row(before, siblingOutcome));
            await Assert.That(Row(after, controlOutcome)).IsEqualTo(Row(before, controlOutcome));
        }
    }

    private static string? Row(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
    {
        var prefix = Convert.ToHexString(key) + ":";
        return cut.Rows.SingleOrDefault(row => row.StartsWith(prefix, StringComparison.Ordinal));
    }
}
