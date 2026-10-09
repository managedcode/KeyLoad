using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum PartitionMovementCapturePointerFault { Missing, AbsentRow, NonCaptureRow }

/// <summary>Only stopped, file-locked fixture roots are opened; every native owner is joined on all paths.</summary>
internal static class PartitionMovementCapturePointerRf3Fault
{
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";

    internal static void Apply(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3NativeCut[] stopped,
        PartitionMovementCapturePointerFault fault, Guid actualAdvanceId)
    {
        foreach (var cut in stopped.Where(cut => cut.Header is not null))
        {
            var header = cut.Header!;
            Guid? pointer = fault switch
            {
                PartitionMovementCapturePointerFault.Missing => null,
                PartitionMovementCapturePointerFault.AbsentRow => MissingIdentity(cut, header),
                PartitionMovementCapturePointerFault.NonCaptureRow => actualAdvanceId,
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            UseStore(wave, cut.Node, DatabaseDirectory, store => store.Commit((transaction, _) =>
            {
                PartitionMoveParentStorage.WriteHeader(transaction, header with { OriginalCapturePhaseCommandId = pointer },
                    IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes);
                return true;
            }));
        }
    }

    internal static async Task RequireHeaderOnlyAsync(PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after, PartitionMovementCapturePointerFault fault, Guid advance)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (original.Header is { } header)
            {
                var pointer = actual.Header!.OriginalCapturePhaseCommandId;
                await SqlRf3Protocol.EqualAsync(header with { OriginalCapturePhaseCommandId = pointer }, actual.Header);
                await RequirePointerVariantAsync(original, header, pointer, fault, advance);
                allowed.Add(Convert.ToHexString(PartitionMoveParentKeys.Header(header.Partition, header.MoveId)));
            }
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(original, actual, allowed);
            var delta = original.Header is null ? PartitionMoveProtocol.EmptyCount : PartitionMoveProtocol.SequenceStep;
            await Assert.That(actual.StorePosition - original.StorePosition).IsEqualTo((long)delta);
        }
    }

    private static async Task RequirePointerVariantAsync(PartitionMovementPublicParentRf3NativeCut original,
        PartitionMoveParentHeader header, Guid? pointer, PartitionMovementCapturePointerFault fault, Guid advance)
    {
        if (fault == PartitionMovementCapturePointerFault.Missing)
        { await Assert.That(pointer).IsNull(); }
        else if (fault == PartitionMovementCapturePointerFault.NonCaptureRow)
        { await Assert.That(pointer).IsEqualTo((Guid?)advance); }
        else
        {
            await Assert.That(pointer).IsNotNull();
            var selected = PartitionMoveParentKeys.Phase(header.Partition, header.MoveId, pointer!.Value);
            await Assert.That(Rows(original).ContainsKey(Convert.ToHexString(selected))).IsFalse();
        }
    }

    private static Guid MissingIdentity(PartitionMovementPublicParentRf3NativeCut cut, PartitionMoveParentHeader header)
    {
        var identity = Guid.NewGuid();
        var key = Convert.ToHexString(PartitionMoveParentKeys.Phase(header.Partition, header.MoveId, identity));
        if (Rows(cut).ContainsKey(key))
        { throw new InvalidOperationException("The missing pointer unexpectedly identifies a real row."); }
        return identity;
    }

    internal static Dictionary<string, string> Rows(PartitionMovementPublicParentRf3NativeCut cut)
        => cut.Rows.ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)], row => row[(row.IndexOf(':', StringComparison.Ordinal) + PartitionMoveProtocol.SequenceStep)..],
            StringComparer.Ordinal);

    internal static T Read<T>(PartitionMovementPublicParentRf3NativeCut cut, byte[] key)
        => NativeSerialization.Deserialize<T>(Convert.FromHexString(Rows(cut)[Convert.ToHexString(key)]));

    internal static long Applied(PartitionMovementPublicParentRf3NativeCut cut)
        => Read<long>(cut, KeySpace.AppliedBytes);

    internal static ReplicaEntry[] AppliedEntries(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after)
        => UseStore(wave, after.Node, ReplicaDirectory, store => store.Read(view =>
        {
            var first = Applied(before);
            var last = Applied(after);
            if (last < first || last - first > IntegrationExecutionOptions.DatabaseLimits().Value.MaxScanRecords)
            { throw new InvalidOperationException("The actual applied journal delta exceeds its original bounded read."); }
            var hard = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(view.ReadOwnedValue(KeyCodec.Encode(ReplicaProtocol.StateKey))
                ?? throw new InvalidOperationException("The real replica authority is absent."));
            if (hard.CommittedIndex < last)
            { throw new InvalidOperationException("The applied delta is not acknowledged by this native owner."); }
            var entries = new List<ReplicaEntry>();
            for (var offset = PartitionMoveProtocol.SequenceStep; offset <= last - first; offset++)
            {
                var index = checked(first + offset);
                var entry = ReplicaProtocolCodec.Deserialize<ReplicaEntry>(view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index))
                    ?? throw new InvalidOperationException("The actual newly applied journal entry is absent."));
                if (entry.Index != index)
                { throw new InvalidOperationException("The native journal index is inconsistent."); }
                entries.Add(entry);
            }
            return entries.ToArray();
        }));

    private static T UseStore<T>(TwoRf3MembershipWave wave, string node, string directory, Func<ZoneTreeStore, T> operation)
    {
        ZoneTreeStore? store = null;
        T result = default!;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, directory)),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            result = operation(store);
        }, failures);
        if (store is { } owner)
        { ServerFailureObserver.Observe(owner.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
