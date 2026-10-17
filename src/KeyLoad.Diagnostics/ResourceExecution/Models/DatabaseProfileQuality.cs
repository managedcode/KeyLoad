namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Sticky degradation makes a profile unavailable for qualification.</summary>
[Flags]
public enum DatabaseProfileQuality
{
    /// <summary>No degradation has been observed.</summary>
    None = 0,
    /// <summary>A producer supplied a dimension outside the closed schema.</summary>
    InvalidDimension = 1,
    /// <summary>A producer supplied an invalid elapsed timestamp.</summary>
    InvalidElapsed = 2,
    /// <summary>A real counter reached its non-wrapping ceiling.</summary>
    SaturatedCounter = 4,
    /// <summary>Four compare-exchange attempts could not publish a record.</summary>
    ContentionDropped = 8,
    /// <summary>A cumulative snapshot merge overflowed and saturated.</summary>
    SnapshotOverflow = 16
}
