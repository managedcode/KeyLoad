namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Reports a bounded local cache configuration or binding attempt without database authority.</summary>
public enum ZoneTreePointCacheControlResult
{
    /// <summary>The opened-store factory installed its sole, initially cold control owner.</summary>
    Created,
    /// <summary>The exact eligible acceptance has a published ready helper at the observation point.</summary>
    Applied,
    /// <summary>The exact acceptance already owns a ready helper, without resetting its entries or counters.</summary>
    AlreadyApplied,
    /// <summary>An embedded cache or existing control already owns this runtime's configuration.</summary>
    AlreadyConfigured,
    /// <summary>The physical writer was unavailable, or an eligible binding changed before final publication.</summary>
    Busy,
    /// <summary>The supplied complete acceptance is altered, stale, withdrawn or expired.</summary>
    Rejected,
    /// <summary>The exact eligible acceptance remains cold because its index reservation was unavailable.</summary>
    Unavailable,
    /// <summary>The runtime or control permanently closed admission; this owner cannot be retried.</summary>
    Closed
}
