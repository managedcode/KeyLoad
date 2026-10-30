namespace KeyLoad.Clustering;

/// <summary>Borrows local execution resources while their physical owner retains every file handle.</summary>
public interface ILocalDatabaseRuntime
{
    /// <summary>Runs one request on its own Orleans context while joining the physical session's work ledger.</summary>
    Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, string? actorKey,
        IGrainContext context, CancellationToken cancellationToken);

    /// <summary>Acquires the shared files for this activation and awaits the original physical opening.</summary>
    Task<ISharedDatabaseSession> OpenSharedAsync(IGrainContext owner, CancellationToken cancellationToken);

    /// <summary>Joins the original local borrowers and closes this activation's exact physical session.</summary>
    Task CloseSharedAsync(ISharedDatabaseSession session);
}
