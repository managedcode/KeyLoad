using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Local-only access to one open physical generation retained by its ordinary database grain.</summary>
public interface ISharedDatabaseSession
{
    /// <summary>The actual execution silo generation holding the file handles.</summary>
    SiloAddress Address { get; }

    /// <summary>Bounded background reads against these same files.</summary>
    IBackgroundStorageOperations Background { get; }

    /// <summary>Returns the canonical sequence persisted in these files.</summary>
    SiloStorageCut ReadCut(CancellationToken cancellationToken);

    /// <summary>Prepares and persists one command through the original atomic storage gate.</summary>
    Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);

    /// <summary>Awaits the original local request Task, using the currently executing grain context.</summary>
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, string? actorKey,
        IGrainContext owner, CancellationToken cancellationToken);

    /// <summary>Cancels and joins the actual maintenance work in this session.</summary>
    Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId);

    /// <summary>Observes the actual physical owner after current administrator admission.</summary>
    Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken);

    /// <summary>Observes the physical owner's bounded admission pools.</summary>
    Task<NodeAdmissionStatus> ReadAdmissionAsync(CancellationToken cancellationToken);

    /// <summary>Observes the actual physical files and their owner.</summary>
    Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken);

    /// <summary>Creates a backup within the original physical lifetime.</summary>
    Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken);
}
