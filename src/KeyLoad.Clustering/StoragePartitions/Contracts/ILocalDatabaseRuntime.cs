using System.Collections.Immutable;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Borrows local execution resources while their physical owner retains every file handle.</summary>
public interface ILocalDatabaseRuntime
{
    /// <summary>Runs one request on its own Orleans context while joining the physical session's work ledger.</summary>
    Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, string? actorKey,
        IGrainContext context, CancellationToken cancellationToken);

    /// <summary>Borrows owned storage for one independently joined primary publication.</summary>
    Task<StorageCopyBatchAcknowledgement> CommitBatchAsync(Guid requestId,
        ImmutableArray<Command<DatabaseCommand>> operations, long firstSequence, bool retainReplication,
        CancellationToken cancellationToken);

    /// <summary>Borrows isolated storage for one independently joined replica publication.</summary>
    Task<StorageCopyBatchAcknowledgement> ApplyReplicaBatchAsync(Guid requestId, StorageReplicaBatch batch, CancellationToken cancellationToken);

    /// <summary>Acquires the shared files for this activation and awaits the original physical opening.</summary>
    Task<ISharedDatabaseSession> OpenSharedAsync(IGrainContext owner, CancellationToken cancellationToken);

    /// <summary>Joins the original local borrowers and closes this activation's exact physical session.</summary>
    Task CloseSharedAsync(ISharedDatabaseSession session);
}
