using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using Orleans.Services;
using ManagedCode.Communication.CQRS;

using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Typed Orleans access to one silo's exclusively owned ZoneTree database.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
public interface ISiloDatabaseService : IGrainService
{
    /// <summary>Reads the durable local cut without changing storage.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Reads an exact admitted retry after current persisted authorization and fingerprint checks.</summary>
    Task<StoredCommand?> ReadStoredCommandAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Evaluates business time and deterministic command decisions at the selected store.</summary>
    Task<Command<DatabaseCommand>> PrepareAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken);
    /// <summary>Applies exactly the prepared operation through ZoneTree's atomic commit gate.</summary>
    Task<StorageOperationResult> ApplyAsync(Command<DatabaseCommand> operation, long sequence, CancellationToken cancellationToken);
    /// <summary>Runs native CQRS execution on this store's local stateless-worker pool.</summary>
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken);
    /// <summary>Executes the original partition-ordered command on this silo's command-worker pool.</summary>
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
