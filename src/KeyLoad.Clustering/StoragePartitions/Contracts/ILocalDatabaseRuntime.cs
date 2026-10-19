namespace KeyLoad.Clustering;

/// <summary>Opens local files for one actual Orleans database activation; no handle crosses an RPC.</summary>
public interface ILocalDatabaseRuntime
{
    /// <summary>Acquires the shared files for this activation and awaits the original physical opening.</summary>
    Task<ISharedDatabaseSession> OpenSharedAsync(IGrainContext owner, CancellationToken cancellationToken);

    /// <summary>Joins the original local borrowers and closes this activation's exact physical session.</summary>
    Task CloseSharedAsync(ISharedDatabaseSession session);
}
