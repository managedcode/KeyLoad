using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeColdAssertions
{
    internal static async Task RequireUnchangedAsync(IReadOnlyDictionary<string, NativeBlobWholeOwnerCut> before,
        IReadOnlyDictionary<string, NativeBlobWholeOwnerCut> after, CancellationToken token)
    {
        await Assert.That(before.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(after.Count).IsEqualTo(before.Count);
        await Assert.That(before.Keys.Order(StringComparer.Ordinal).SequenceEqual(
            TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(after.Keys.Order(StringComparer.Ordinal).SequenceEqual(
            before.Keys.Order(StringComparer.Ordinal))).IsTrue();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            token.ThrowIfCancellationRequested();
            var left = before[node];
            var right = after[node];
            await RequireIdentityAsync(left, right);
            await RequireHistoryAsync(left, right, token);
            var delta = right.Entries.Where(entry => entry.Index > left.Applied && entry.Index <= right.Applied).ToArray();
            await PartitionMovementBlobWireNativeCut.RequireEntriesAsync(left.OriginalCut, right.OriginalCut, delta);
            var allowed = await AllowedNewRowsAsync(left, right, token);
            RequireRawJournal(left.ReplicaRows, right.ReplicaRows, allowed, token);
        }
    }

    private static async Task RequireIdentityAsync(NativeBlobWholeOwnerCut left, NativeBlobWholeOwnerCut right)
    {
        await Assert.That(left.CanonicalNodeId != Guid.Empty && left.ReplicaNodeId != Guid.Empty
            && left.CanonicalNodeId != left.ReplicaNodeId).IsTrue();
        await Assert.That(right.CanonicalNodeId).IsEqualTo(left.CanonicalNodeId);
        await Assert.That(right.ReplicaNodeId).IsEqualTo(left.ReplicaNodeId);
        await Assert.That(right.Owner.Incarnation).IsEqualTo(left.Owner.Incarnation);
        await Assert.That(right.Owner.VoterId).IsEqualTo(left.Owner.VoterId);
        await Assert.That(right.Owner.ClusterId).IsEqualTo(left.Owner.ClusterId);
        await Assert.That(right.Owner.SiloAddress).IsNotEqualTo(left.Owner.SiloAddress);
        await Assert.That(right.HardState.Term >= left.HardState.Term).IsTrue();
        await Assert.That(right.ReplicaPosition >= left.ReplicaPosition).IsTrue();
        await Assert.That(right.HardState.CommittedIndex >= left.HardState.CommittedIndex).IsTrue();
        await Assert.That(right.HardState.LastIndex >= left.HardState.LastIndex).IsTrue();
        await Assert.That(right.Applied >= left.Applied).IsTrue();
    }

    private static async Task RequireHistoryAsync(NativeBlobWholeOwnerCut left, NativeBlobWholeOwnerCut right,
        CancellationToken token)
    {
        await Assert.That(left.HardState.Snapshot).IsNull();
        await Assert.That(right.HardState.Snapshot).IsNull();
        await Assert.That(left.Applied).IsEqualTo(left.HardState.CommittedIndex);
        await Assert.That(right.Applied).IsEqualTo(right.HardState.CommittedIndex);
        await Assert.That((long)left.Entries.Length).IsEqualTo(left.HardState.LastIndex);
        await Assert.That((long)right.Entries.Length).IsEqualTo(right.HardState.LastIndex);
        for (var index = 0; index < right.Entries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(right.Entries[index].Index).IsEqualTo((long)index + 1);
            if (index < left.Entries.Length)
            { await Assert.That(left.Entries[index].Index).IsEqualTo((long)index + 1); }
        }
    }

    private static async Task<HashSet<string>> AllowedNewRowsAsync(NativeBlobWholeOwnerCut left,
        NativeBlobWholeOwnerCut right, CancellationToken token)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        { Convert.ToHexString(ReplicaProtocol.StateStorageKey) };
        foreach (var entry in right.Entries.Where(entry => entry.Index > left.HardState.LastIndex))
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(entry.Index <= right.HardState.CommittedIndex).IsTrue();
            await Assert.That(entry.Operation is null || entry.Operation.Kind == OperationKind.Membership).IsTrue();
            allowed.Add(Convert.ToHexString(ReplicaProtocol.EntryStorageKey(entry.Index)));
        }
        return allowed;
    }

    private static void RequireRawJournal(string[] before, string[] after, HashSet<string> allowed,
        CancellationToken token)
    {
        var left = Rows(before);
        var right = Rows(after);
        foreach (var key in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (allowed.Contains(key)) { continue; }
            if (!left.TryGetValue(key, out var original) || !right.TryGetValue(key, out var actual) || original != actual)
            { throw new InvalidOperationException("The complete original Blob journal changed outside native membership authority."); }
        }
    }

    private static Dictionary<string, string> Rows(string[] rows)
        => rows.ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)],
            row => row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
}
