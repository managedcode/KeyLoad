namespace KeyLoad;

/// <summary>Selects construction, native incremental recovery, or fenced release.</summary>
public enum TextIndexMaintenanceMode
{
    /// <summary>Capture one complete canonical seed and construct a new generation.</summary>
    Build,
    /// <summary>Load the existing native generation and replay only its pinned canonical interval.</summary>
    Restore,
    /// <summary>Fence the native generation and release its canonical projection pin.</summary>
    Release
}
