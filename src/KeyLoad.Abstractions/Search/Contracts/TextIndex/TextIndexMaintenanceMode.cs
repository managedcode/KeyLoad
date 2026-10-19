namespace KeyLoad;

/// <summary>Selects construction, execution incremental recovery, or fenced release.</summary>
public enum TextIndexMaintenanceMode
{
    /// <summary>Capture one complete canonical seed and construct a new generation.</summary>
    Build,
    /// <summary>Load the existing execution generation and replay only its pinned canonical interval.</summary>
    Restore,
    /// <summary>Fence the execution generation and release its canonical projection pin.</summary>
    Release
}
