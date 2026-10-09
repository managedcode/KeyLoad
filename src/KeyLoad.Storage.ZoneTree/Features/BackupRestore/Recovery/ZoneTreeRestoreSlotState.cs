using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reads genuine operation-owned transaction evidence; no CLI flag can stand in for a row.</summary>
internal static class ZoneTreeRestoreSlotState
{
    private const string Invalid = "The recovered native restore slot transaction evidence is inconsistent.";

    internal static ClusterRestoreSlotCommit Require(IKeyValueView view, ClusterRestoreSlotContext context,
        ClusterRestoreSlotCommitKind kind, long actualPosition)
    {
        var key = kind == ClusterRestoreSlotCommitKind.Reconciled
            ? ClusterRestoreSlotKeys.Reconciled() : ClusterRestoreSlotKeys.AuthorityReset();
        var row = view.GetRecord<ClusterRestoreSlotCommit>(key);
        if (row is null || row.Version != ClusterRestoreSlotCommit.CurrentVersion || row.Context != context
            || row.Kind != kind || row.OriginalCut is null || row.OriginalCut.SourceNodeId != context.SourceNodeId
            || row.OriginalCut.StorePosition != context.SourcePosition
            || row.NativeCommitPosition <= context.SourcePosition || row.NativeCommitPosition > actualPosition
            || string.IsNullOrWhiteSpace(row.MappingsDigest))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return row;
    }

    internal static void RequireTarget(StoreIdentity identity, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey)
    {
        if (identity.NodeId != context.TargetNodeId || identity.Incarnation != context.TargetIncarnation
            || !identity.DispatchPaused || !CryptographicOperations.FixedTimeEquals(identity.SigningKey.Span, signingKey.Span)
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(signingKey.Span)),
                context.TargetSignerFingerprint, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }

    internal static void Reset(IAtomicTransaction transaction, ClusterRestoreSlotContext context,
        ClusterRestoreSlotCommit reconciled, long position, CancellationToken ct)
    {
        if (position <= reconciled.NativeCommitPosition)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        ZoneTreeRestoreSlotOrigins.Stage(transaction, context, reconciled.OriginalCut.AppliedIndex, ct);
        transaction.Delete(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
        transaction.Delete(KeyCodec.Encode(SystemNamespace, ClockKey));
        transaction.Delete(KeyCodec.Encode(MembershipNamespace, OrleansMembershipKey));
        transaction.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedKey), true);
        transaction.PutRecord(ClusterRestoreSlotKeys.AuthorityReset(), reconciled with
        { NativeCommitPosition = position, Kind = ClusterRestoreSlotCommitKind.AuthorityReset });
        ct.ThrowIfCancellationRequested();
        transaction.ValidateCommit();
    }
}
