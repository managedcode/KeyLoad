namespace KeyLoad;

/// <summary>Identifies the stable error category returned by KeyLoad operations.</summary>
public enum ErrorCode
{
    /// <summary>The request or persisted value is invalid.</summary>
    Validation,
    /// <summary>The requested resource does not exist.</summary>
    NotFound,
    /// <summary>The requested change conflicts with current state.</summary>
    Conflict,
    /// <summary>The expected revision does not match current state.</summary>
    RevisionConflict,
    /// <summary>An event identifier is already present.</summary>
    DuplicateEventId,
    /// <summary>The authenticated principal is not permitted to perform the operation.</summary>
    PermissionDenied,
    /// <summary>The request has no valid authenticated identity.</summary>
    Unauthenticated,
    /// <summary>A configured resource capacity has been exhausted.</summary>
    ResourceExhausted,
    /// <summary>An operation exceeded its configured work budget.</summary>
    BudgetExceeded,
    /// <summary>The requested capability is not supported.</summary>
    UnsupportedCapability,
    /// <summary>The write outcome cannot be determined safely.</summary>
    UnknownWriteOutcome,
    /// <summary>Recovery must complete before the operation can proceed.</summary>
    RecoveryRequired,
    /// <summary>The supplied position token is no longer valid.</summary>
    TokenInvalidated,
    /// <summary>The supplied query cursor has expired.</summary>
    CursorExpired,
    /// <summary>The requested history is no longer available.</summary>
    HistoryUnavailable,
    /// <summary>The required index is not ready at the requested cut.</summary>
    IndexNotReady,
    /// <summary>The delivery lease has expired.</summary>
    LeaseExpired,
    /// <summary>The supplied delivery lease is stale.</summary>
    StaleLease,
    /// <summary>Dispatch is paused for the resource.</summary>
    DispatchPaused,
    /// <summary>This node no longer owns the resource.</summary>
    OwnershipLost,
    /// <summary>Persisted data is invalid or inconsistent.</summary>
    Corruption,
    /// <summary>The persisted format is not supported.</summary>
    FormatUnsupported,
    /// <summary>The clock cannot safely establish the required time.</summary>
    ClockUncertain,
    /// <summary>The operation was cancelled.</summary>
    Cancelled
}
