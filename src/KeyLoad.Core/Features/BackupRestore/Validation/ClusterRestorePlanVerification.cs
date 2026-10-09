namespace KeyLoad.Core;

/// <summary>Checks full original capture evidence independently of caller object alias layout.</summary>
public static class ClusterRestorePlanVerification
{
    private const string Invalid = "The verified native source cut differs from the admitted original restore cut.";

    /// <summary>Requires every original owner, catalog, roster, placement, canonical digest and cut field to match.</summary>
    /// <param name="expected">Original checksum-bound admitted native capture.</param>
    /// <param name="actual">Freshly verified native archive capture.</param>
    public static void RequireOriginalCut(ClusterBackupOwnerCut expected, ClusterBackupOwnerCut actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (!ClusterBackupMetadataEquality.OwnerCut(expected, actual))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
