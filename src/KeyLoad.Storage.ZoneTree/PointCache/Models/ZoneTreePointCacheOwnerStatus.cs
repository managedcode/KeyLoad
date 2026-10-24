namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>Reports a local zero-wait physical owner observation without granting a lease.</summary>
public enum ZoneTreePointCacheOwnerStatus
{
    /// <summary>The actual read gate and execution health check permitted a point-in-time identity observation.</summary>
    Healthy,
    /// <summary>The actual gate was busy or the current thread already owns a storage operation.</summary>
    Busy,
    /// <summary>The actual store is closing or this control permanently closed admission.</summary>
    Closed
}
