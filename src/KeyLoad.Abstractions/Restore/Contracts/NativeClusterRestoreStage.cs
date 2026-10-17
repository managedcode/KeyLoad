namespace KeyLoad;

/// <summary>Actual post-barrier diagnostic observation only; no stage value supplies restore authority.</summary>
public enum NativeClusterRestoreStage
{
    /// <summary>No actual native process boundary; not a legal observed stage.</summary>
    None = 0,
    /// <summary>Original immutable plan flushed, renamed and reread.</summary>
    PlanPublished = 1,
    /// <summary>Exact original source prefix fully copied and synchronously flushed.</summary>
    SourcePrefixPersisted = 2,
    /// <summary>Native reconciliation and its completion row committed together.</summary>
    Reconciled = 3,
    /// <summary>Actual admitted target identity file published after original store join.</summary>
    IdentityPublished = 4,
    /// <summary>Actual native origin/reset changes and their completion row committed together.</summary>
    AuthorityReset = 5,
    /// <summary>Full native Ready progress flushed, renamed and reread.</summary>
    ReadyPublished = 6,
    /// <summary>Single complete nodes-directory rename actually completed.</summary>
    NodesPublished = 7
}
