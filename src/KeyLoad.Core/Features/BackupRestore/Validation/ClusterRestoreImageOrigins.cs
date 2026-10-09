using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class ClusterRestoreImageOrigins
{
    private const long NoAppliedIndex = 0;
    private const string MembershipSpace = "membership";
    private const string MembershipName = "orleans-membership";
    private const string SystemSpace = "system";
    private const string PausedName = "dispatch-paused";
    private const string Invalid = "The recovered native authority reset or immutable historical origin is inconsistent.";
    private static readonly byte[] MembershipKey = KeyCodec.Encode(MembershipSpace, MembershipName);
    private static readonly byte[] PausedKey = KeyCodec.Encode(SystemSpace, PausedName);

    internal static void Require(ClusterRestoreImageState state)
    {
        var paused = state.Target.ReadOwnedValue(PausedKey);
        if (state.Target.ReadOwnedValue(KeySpace.AppliedBytes) is not null
            || state.Target.ReadOwnedValue(KeySpace.ClockBytes) is not null
            || state.Target.ReadOwnedValue(MembershipKey) is not null
            || paused is null || !NativeSerialization.Deserialize<bool>(paused))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var hasOrigins = false;
        foreach (var partition in state.Original.Partitions)
        {
            state.Work.Check();
            var entry = partition.Roster;
            var actual = state.Target.GetRecord<AtomicPartitionRosterRestoreOrigin>(
                AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition));
            if (entry.FirstSeenAppliedIndex <= NoAppliedIndex)
            {
                if (actual is not null)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                continue;
            }
            hasOrigins = true;
            var key = AtomicPartitionRosterRestoreOriginSerialization.EntryKey(entry.Partition);
            var originalBytes = state.Source.ReadOwnedValue(key)
                ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
            var previous = state.Source.GetRecord<AtomicPartitionRosterRestoreOrigin>(
                AtomicPartitionRosterRestoreOriginSerialization.OriginKey(entry.Partition));
            var bound = Math.Max(state.Original.AppliedIndex, previous?.AppliedUpperBound ?? NoAppliedIndex);
            if (actual is null || actual.Version != AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion
                || actual.Partition != entry.Partition || actual.SourceIncarnation != state.Context.SourceIncarnation
                || actual.RestoredIncarnation != state.Context.TargetIncarnation || actual.AppliedUpperBound != bound
                || !CryptographicOperations.FixedTimeEquals(actual.EntryDigest.Span, SHA256.HashData(originalBytes)))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
        var identity = state.Target.GetRecord<AtomicPartitionRosterRestoreIdentity>(
            AtomicPartitionRosterRestoreOriginSerialization.IdentityKey());
        if (hasOrigins ? identity is null || identity.Version != AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion
                || identity.SourceIncarnation != state.Context.SourceIncarnation
                || identity.RestoredIncarnation != state.Context.TargetIncarnation : identity is not null)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }

    internal static List<byte[]> ChangedKeys(ClusterRestoreImageState state)
    {
        List<byte[]> keys = [KeySpace.AppliedBytes, KeySpace.ClockBytes, MembershipKey, PausedKey,
            AtomicPartitionRosterRestoreOriginSerialization.IdentityKey()];
        foreach (var partition in state.Original.Partitions)
        { keys.Add(AtomicPartitionRosterRestoreOriginSerialization.OriginKey(partition.Roster.Partition)); }
        return keys;
    }
}
