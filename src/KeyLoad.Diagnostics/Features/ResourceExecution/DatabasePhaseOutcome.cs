namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Closed completed-scope outcomes; producer-owned results determine them.</summary>
public enum DatabasePhaseOutcome
{
    /// <summary>The real owned work completed successfully.</summary>
    Completed = 0,
    /// <summary>The real result rejected the requested work.</summary>
    Rejected = 1,
    /// <summary>Bounded admission rejected the attempt as busy.</summary>
    Busy = 2,
    /// <summary>An observed cancellation has a cancelled phase-local input token.</summary>
    CallerCancelled = 3,
    /// <summary>The operation deadline or physical host stopping caused cancellation.</summary>
    DeadlineOrStopping = 4,
    /// <summary>The owned scope failed without a more specific original outcome.</summary>
    Faulted = 5
}
