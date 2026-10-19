namespace KeyLoad;

/// <summary>Names bounded maintenance phases without payload or credentials.</summary>
public enum AnnMaintenancePhase
{
    /// <summary>Admit and pin the canonical projection consumer.</summary>
    Configure,
    /// <summary>Retain one complete bounded canonical seed and release the raw read view.</summary>
    Capture,
    /// <summary>Load or construct exact execution arrays.</summary>
    RuntimeIndex,
    /// <summary>Apply only the admitted signed outbox interval.</summary>
    Replay,
    /// <summary>Verify the exact canonical upper source cut.</summary>
    Verify,
    /// <summary>Flush and publish one immutable generation.</summary>
    Publish,
    /// <summary>Observe the actual canonical projection checkpoint receipt.</summary>
    Checkpoint,
    /// <summary>Fence and release an obsolete generation.</summary>
    Release,
    /// <summary>The bounded maintenance operation completed.</summary>
    Completed
}
