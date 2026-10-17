namespace KeyLoad;

/// <summary>Closed native offline commit stages, not caller-selected execution authority.</summary>
public enum ClusterRestoreSlotCommitKind
{
    /// <summary>No admitted native slot commit; rejected by owning validation.</summary>
    None = 0,
    /// <summary>Original source-cut catalog transformation committed at its actual native cut.</summary>
    Reconciled = 1,
    /// <summary>Original new-target authority reset committed atomically with its native stage row.</summary>
    AuthorityReset = 2
}
