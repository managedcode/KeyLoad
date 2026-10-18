namespace KeyLoad.Clustering;

/// <summary>How Orleans storage owners access the configured physical files.</summary>
public enum StorageAccessMode
{
    /// <summary>Each silo owns separate files; copies require independent persistence acknowledgements.</summary>
    IsolatedVolumes,
    /// <summary>Silos can reach the same files, with one open mutable owner and no additional copies.</summary>
    SharedVolume
}
