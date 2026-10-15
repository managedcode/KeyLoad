using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3ColdAssertions
{
    internal static async Task RequireAsync(IReadOnlyDictionary<string, NativeBlobWholeOwnerCut> before,
        IReadOnlyDictionary<string, NativeBlobWholeOwnerCut> after, QueuePolicyEpochRf3Seed seed, QueuePolicyEpochRf3Known[] known, CancellationToken token)
    {
        await Assert.That(before.Keys.Order(StringComparer.Ordinal).SequenceEqual(
            TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(after.Keys.Order(StringComparer.Ordinal).SequenceEqual(before.Keys.Order(StringComparer.Ordinal))).IsTrue();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            token.ThrowIfCancellationRequested();
            var left = before[node];
            var right = after[node];
            await IdentityAsync(left, right);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            var clock = new PartitionMovementNativeColdClockRf3Cut(left.OriginalCut.Rows, right.OriginalCut.Rows, allowed);
            for (var index = left.Applied + 1; index <= right.Applied; index++)
            {
                token.ThrowIfCancellationRequested();
                var entry = right.Entries.Single(value => value.Index == index);
                if (entry.Operation is not { } operation)
                {
                    continue;
                }
                await QueuePolicyEpochRf3ReplayProof.RequireAsync(left, operation, seed, known, token);
            }
            if (right.Applied != left.Applied)
            {
                allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes));
            }
            await clock.RequireCompleteAsync();
            ExactRows(left.CanonicalRows, right.CanonicalRows, allowed, token);
            await Assert.That(right.CanonicalPosition - left.CanonicalPosition).IsEqualTo(right.Applied - left.Applied);
            var journal = new HashSet<string>(StringComparer.Ordinal) { Convert.ToHexString(ReplicaProtocol.StateStorageKey) };
            foreach (var entry in right.Entries.Where(value => value.Index > left.HardState.LastIndex))
            {
                token.ThrowIfCancellationRequested();
                await Assert.That(entry.Index <= right.HardState.CommittedIndex).IsTrue();
                if (entry.Operation is { } operation)
                {
                    await QueuePolicyEpochRf3ReplayProof.RequireAsync(left, operation, seed, known, token);
                }
                journal.Add(Convert.ToHexString(ReplicaProtocol.EntryStorageKey(entry.Index)));
            }
            ExactRows(left.ReplicaRows, right.ReplicaRows, journal, token);
        }
    }

    private static async Task IdentityAsync(NativeBlobWholeOwnerCut left, NativeBlobWholeOwnerCut right)
    {
        await Assert.That(left.CanonicalNodeId != Guid.Empty && left.ReplicaNodeId != Guid.Empty
            && left.CanonicalNodeId != left.ReplicaNodeId).IsTrue();
        await Assert.That(right.CanonicalNodeId).IsEqualTo(left.CanonicalNodeId);
        await Assert.That(right.ReplicaNodeId).IsEqualTo(left.ReplicaNodeId);
        await Assert.That(right.Owner.VoterId).IsEqualTo(left.Owner.VoterId);
        await Assert.That(right.Owner.Incarnation).IsEqualTo(left.Owner.Incarnation);
        await Assert.That(right.Owner.ClusterId).IsEqualTo(left.Owner.ClusterId);
        await Assert.That(right.Owner.SiloAddress).IsNotEqualTo(left.Owner.SiloAddress);
        await Assert.That(right.HardState.Term >= left.HardState.Term).IsTrue();
        await Assert.That(right.ReplicaPosition >= left.ReplicaPosition).IsTrue();
        await Assert.That(right.Applied >= left.Applied).IsTrue();
        await Assert.That(left.HardState.Snapshot).IsNull();
        await Assert.That(right.HardState.Snapshot).IsNull();
        await Assert.That(left.Applied).IsEqualTo(left.HardState.CommittedIndex);
        await Assert.That(right.Applied).IsEqualTo(right.HardState.CommittedIndex);
        await Assert.That((long)left.Entries.Length).IsEqualTo(left.HardState.LastIndex);
        await Assert.That((long)right.Entries.Length).IsEqualTo(right.HardState.LastIndex);
        await Assert.That(right.HardState.LastIndex >= left.HardState.LastIndex).IsTrue();
        for (var index = 0; index < right.Entries.Length; index++)
        {
            await Assert.That(right.Entries[index].Index).IsEqualTo((long)index + 1);
        }
    }

    private static void ExactRows(string[] original, string[] current, HashSet<string> allowed, CancellationToken token)
    {
        var left = Rows(original);
        var right = Rows(current);
        foreach (var key in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!allowed.Contains(key) && (!left.TryGetValue(key, out var value)
                || !right.TryGetValue(key, out var actual) || value != actual))
            {
                throw new InvalidOperationException("The original complete queue policy-epoch cut changed outside exact native replay authority.");
            }
        }
    }

    private static Dictionary<string, string> Rows(string[] rows)
        => rows.ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)],
            row => row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
}
