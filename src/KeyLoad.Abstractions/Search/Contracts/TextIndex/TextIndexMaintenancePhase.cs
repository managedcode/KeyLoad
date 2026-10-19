namespace KeyLoad;

/// <summary>Names actual bounded work phases without private document data.</summary>
public enum TextIndexMaintenancePhase
{
    /// <summary>Admit an exact persisted consumer and source owner.</summary>
    Configure,
    /// <summary>Capture a complete bounded canonical seed or recovery authority.</summary>
    Capture,
    /// <summary>Persist original execution page work before changing postings.</summary>
    Intent,
    /// <summary>Apply the actual execution index changes.</summary>
    RuntimeIndex,
    /// <summary>Join execution handles and publish verified complete inventory.</summary>
    Publish,
    /// <summary>Observe the original canonical checkpoint child receipt.</summary>
    Checkpoint,
    /// <summary>Fence and release the exact obsolete consumer generation.</summary>
    Release,
    /// <summary>All bounded work and its actual canonical receipts completed.</summary>
    Completed
}
