using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class ClusterRestoreImageCommits
{
    private const string Invalid = "The recovered restore slot lacks its exact native transaction lineage.";

    internal static bool Require(IKeyValueView target, ClusterBackupOwnerCut original,
        ClusterRestoreSlotContext context, string mappingsDigest, long position)
    {
        var reconciled = RequireRow(target.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.Reconciled()),
            original, context, mappingsDigest, ClusterRestoreSlotCommitKind.Reconciled);
        var reset = target.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset());
        if (reset is null)
        {
            if (position != reconciled.NativeCommitPosition)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            return false;
        }
        reset = RequireRow(reset, original, context, mappingsDigest, ClusterRestoreSlotCommitKind.AuthorityReset);
        if (position != reset.NativeCommitPosition || reset.NativeCommitPosition <= reconciled.NativeCommitPosition)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return true;
    }

    private static ClusterRestoreSlotCommit RequireRow(ClusterRestoreSlotCommit? row, ClusterBackupOwnerCut original,
        ClusterRestoreSlotContext context, string mappingsDigest, ClusterRestoreSlotCommitKind kind)
    {
        if (row is null || row.Version != ClusterRestoreSlotCommit.CurrentVersion || row.Context != context
            || row.Kind != kind || row.NativeCommitPosition <= context.SourcePosition
            || row.OriginalCut is null || !ClusterBackupMetadataEquality.OwnerCut(original, row.OriginalCut)
            || !string.Equals(row.MappingsDigest, mappingsDigest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return row;
    }
}
