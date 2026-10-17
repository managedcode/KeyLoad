namespace KeyLoad;

/// <summary>Selects explicit construction, exact native restoration, or release of one admin generation.</summary>
public enum AnnMaintenanceMode
{
    /// <summary>Construct a new acceleration generation from the authorized canonical source.</summary>
    Build,
    /// <summary>Load existing native arrays and replay the retained canonical prefix.</summary>
    Restore,
    /// <summary>Fence a generation and release its persisted projection pin.</summary>
    Release
}
