using ManagedCode.Communication.Commands;
using Orleans.CodeGeneration;
using Orleans.Services;

namespace KeyLoad.Clustering;

/// <summary>Typed Orleans access to one silo's exclusively owned ZoneTree database.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
public partial interface ISiloDatabaseService : IGrainService
{
    /// <summary>Reads the durable local cut without changing storage.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Reads an exact admitted retry after current persisted authorization and fingerprint checks.</summary>
    Task<StoredCommand?> ReadStoredCommandAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Evaluates business time and deterministic command decisions at the selected store.</summary>
    Task<Command<DatabaseCommand>> PrepareAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Applies exactly the prepared operation through ZoneTree's atomic commit gate.</summary>
    Task<StorageOperationResult> ApplyAsync(Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken);
    /// <summary>Awaits a self-contained request on the native stateless-worker pool.</summary>
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken);
    /// <summary>Joins a server-owned maintenance session's actual cleanup after cancellation or revocation.</summary>
    Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId);
    /// <summary>Executes the original partition-ordered command through this silo's native worker pool.</summary>
    Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken);
    /// <summary>Observes the exact addressed silo after the caller verified current administrator authority.</summary>
    Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken);
    /// <summary>Reads the addressed silo's bounded admission counters.</summary>
    Task<NodeAdmissionStatus> ReadAdmissionAsync(CancellationToken cancellationToken);
    /// <summary>Reads the addressed silo's actual files and process observation.</summary>
    Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken);
    /// <summary>Creates a backup at the addressed silo's synchronous storage boundary.</summary>
    Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken);
}
