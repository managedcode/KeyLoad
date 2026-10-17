using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationCold
{
    private const int ControlCopies = 3;
    private const long NoGrants = 0;
    private const string RowSeparator = ":";
    internal sealed record Rows(string Pending, string Outcome);

    internal static async Task<Rows> PendingAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3RevocationCaller caller,
        PlacementIntentV1 pending, Guid persistId, Rows? originalRows, CancellationToken token)
    {
        var move = pending.Plan.Moves.Single();
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, move, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(stopped);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, move, stopped);
        var rows = await RequirePendingAsync(stopped, pending, persistId, originalRows);
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        await caller.ReconnectAsync(wave, token);
        return rows;
    }

    internal static async Task<Rows> RequirePendingAsync(PartitionMovementPublicParentRf3NativeCut[] stopped,
        PlacementIntentV1 pending, Guid persistId, Rows? originalRows)
    {
        var prefix = Convert.ToHexString(TenantPlacementPolicyStorage.IntentKey(
            pending.Plan.TenantId, pending.Plan.DatabaseId, pending.IntentId)) + RowSeparator;
        var rows = Select(stopped, prefix);
        await Assert.That(rows.Length).IsEqualTo(ControlCopies);
        var retained = originalRows?.Pending ?? rows.First();
        foreach (var row in rows)
        {
            await Assert.That(row).IsEqualTo(retained);
            var value = NativeSerialization.Deserialize<PlacementIntentV1>(Convert.FromHexString(row[prefix.Length..]));
            await SqlRf3Protocol.EqualAsync(pending, value);
            await Assert.That(value.Status).IsEqualTo(PlacementIntentStatus.Pending);
            await Assert.That(value.Claim).IsNull();
            await Assert.That(value.OriginalApplyCommandId).IsNull();
            await Assert.That(value.OriginalApplyRequest).IsNull();
        }
        var outcome = await OutcomeAsync(stopped, pending, persistId, originalRows?.Outcome);
        foreach (var cut in stopped)
        {
            await Assert.That(cut.Header).IsNull();
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo(NoGrants);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo(NoGrants);
        }
        return new(retained, outcome);
    }

    private static async Task<string> OutcomeAsync(PartitionMovementPublicParentRf3NativeCut[] stopped,
        PlacementIntentV1 pending, Guid persistId, string? original)
    {
        var prefix = Convert.ToHexString(KeySpace.PartitionOutcome(
            pending.Plan.ControlPartition, pending.PrincipalId, persistId)) + RowSeparator;
        var rows = Select(stopped, prefix);
        await Assert.That(rows.Length).IsEqualTo(ControlCopies);
        var retained = original ?? rows.First();
        foreach (var row in rows)
        {
            await Assert.That(row).IsEqualTo(retained);
            var outcome = NativeSerialization.Deserialize<StoredOutcome>(Convert.FromHexString(row[prefix.Length..]));
            await Assert.That(outcome.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
            await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await Assert.That(outcome.Partition).IsEqualTo(pending.Plan.ControlPartition);
            await Assert.That(outcome.Result.Error).IsNull();
            await Assert.That(outcome.Result.SafeDetail).IsNull();
            await SqlRf3Protocol.EqualAsync(pending, outcome.Result.Get<PlacementIntentV1>());
        }
        return retained;
    }

    private static string[] Select(PartitionMovementPublicParentRf3NativeCut[] stopped, string prefix)
        => stopped.Select(cut => cut.Rows.SingleOrDefault(row => row.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(row => row is not null).Cast<string>().ToArray();
}
