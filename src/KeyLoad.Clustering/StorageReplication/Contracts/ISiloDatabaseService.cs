using System.Collections.Immutable;
using ManagedCode.Communication.Commands;
using Orleans.Services;

namespace KeyLoad.Clustering;

/// <summary>Typed Orleans access to one silo's exclusively owned ZoneTree database.</summary>
public partial interface ISiloDatabaseService : IGrainService
{
    /// <summary>Joins one primary publication to the addressed silo's original physical session.</summary>
    Task<StorageCopyBatchAcknowledgement> CommitBatchAsync(Guid requestId,
        ImmutableArray<Command<DatabaseCommand>> operations, long firstSequence, bool retainReplication,
        CancellationToken cancellationToken);
    /// <summary>Publishes the primary's exact effects without preparing commands again.</summary>
    Task<StorageCopyBatchAcknowledgement> ApplyReplicaBatchAsync(Guid requestId, StorageReplicaBatch batch,
        CancellationToken cancellationToken);
    /// <summary>Reads the durable local cut without changing storage.</summary>
    Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken);
    /// <summary>Reads the independent sequence of one command owner.</summary>
    Task<SiloStorageCut> ReadPartitionCutAsync(PartitionRef? partition, CancellationToken cancellationToken);
    /// <summary>Reads the last retained primary effects for an interrupted partition copy.</summary>
    Task<StorageReplicaBatch?> ReadReplicaBatchAsync(PartitionRef? partition, CancellationToken cancellationToken);
    /// <summary>Joins a server-owned maintenance session's actual cleanup after cancellation or revocation.</summary>
    Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId);
    /// <summary>Observes the exact addressed silo after the caller verified current administrator authority.</summary>
    Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken);
    /// <summary>Reads the addressed silo's actual files and process observation.</summary>
    Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken);
    /// <summary>Creates a backup at the addressed silo's synchronous storage boundary.</summary>
    Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken);
}
