namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Closed source-owned database timing boundaries; IDs are stable.</summary>
public enum DatabasePhaseKind
{
    /// <summary>The credential resolver's real read dispatch.</summary>
    PublicAuthenticationDispatch = 0,
    /// <summary>The operation gateway's real node dispatch.</summary>
    PublicOperationDispatch = 1,
    /// <summary>The authorized authentication branch's execution read barrier.</summary>
    AuthorizedAuthenticationBarrier = 2,
    /// <summary>The authorized operation branch's separate execution read barrier.</summary>
    AuthorizedOperationBarrier = 3,
    /// <summary>The authorized read capability invocation.</summary>
    AuthorizedReadCapability = 4,
    /// <summary>The provider's actual Read reader-lock acquisition.</summary>
    ProviderReadGateWait = 27,
    /// <summary>The provider's actual Commit writer-lock acquisition.</summary>
    ProviderWriteGateWait = 28,
    /// <summary>The provider Commit writer-lock hold, including no-change calls.</summary>
    ProviderWriteGateHold = 29,
    /// <summary>Existing atomic redo writes through their Flush(true).</summary>
    AtomicJournalWriteFlush = 30,
    /// <summary>Actual cache/value/execution Sync-WAL/tree mutation work.</summary>
    TreeMutation = 31
}
