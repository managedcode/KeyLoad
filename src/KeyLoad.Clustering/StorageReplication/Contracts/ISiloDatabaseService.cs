using System.Collections.Immutable;
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
    /// <summary>Reads the commands of the last committed batch for completing an interrupted storage copy.</summary>
    Task<ImmutableArray<StoredCommand>> ReadLastCommittedCommandsAsync(CancellationToken cancellationToken);
    /// <summary>Returns retained originals of exact admitted retries and evaluates business time and deterministic
    /// command decisions at the selected store for the prefix that one commit can apply.</summary>
    Task<ImmutableArray<Command<DatabaseCommand>>> PrepareBatchAsync(ImmutableArray<Command<DatabaseCommand>> operations,
        CancellationToken cancellationToken);
    /// <summary>Applies exactly the prepared operations, in order, through one ZoneTree atomic commit.</summary>
    Task<StorageBatchResult> ApplyBatchAsync(ImmutableArray<Command<DatabaseCommand>> operations, long firstSequence,
        CancellationToken cancellationToken);
    /// <summary>Awaits a self-contained request on the execution stateless-worker pool.</summary>
    Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, CancellationToken cancellationToken);
    /// <summary>Joins a server-owned maintenance session's actual cleanup after cancellation or revocation.</summary>
    Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId);
    /// <summary>Executes the original partition-ordered command through this silo's execution worker pool.</summary>
    Task<GrainOperationReply> ExecuteCommandAsync(GrainRequestEnvelope envelope, string actorKey,
        CancellationToken cancellationToken);
    /// <summary>Observes the exact addressed silo after the caller verified current administrator authority.</summary>
    Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken);
    /// <summary>Reads the addressed silo's actual files and process observation.</summary>
    Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken);
    /// <summary>Creates a backup at the addressed silo's synchronous storage boundary.</summary>
    Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken);
}
