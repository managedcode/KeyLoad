using KeyLoad.Core;
using KeyLoad.Core.Messaging;
using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

/// <summary>Bounded storage reads shared by the logical owner and execution silo-local service.</summary>
public interface IBackgroundStorageOperations
{
    /// <summary>Reads one bounded persisted due prefix and its actual continuation.</summary>
    Task<DueWorkDiscoveryResult> ReadDueWorkPageAsync(DueSweepCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads one bounded queue-deadline prefix and its actual continuation.</summary>
    Task<QueueDeadlineDiscoveryResult> ReadQueueDeadlinePageAsync(QueueDeadlineCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads pending transfers using only the configured persisted coordinator.</summary>
    Task<RemoteTransferPendingPage?> ReadQueueTransferPendingAsync(RemoteTransferPendingCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads bounded persisted chunk-work hints.</summary>
    Task<SampleChunkWorkPage> ReadSampleChunkWorkPageAsync(byte[]? after, CancellationToken cancellationToken);
    /// <summary>Reads bounded ANN hints at the selected actual storage owner.</summary>
    Task<BackgroundReadResult> ReadAnnProjectionWorkPageAsync(SiloAddress target, byte[]? after, BackgroundReadBudget budget, CancellationToken cancellationToken);
    /// <summary>Reloads the persisted creator; a genuinely stale saga returns no creator.</summary>
    Task<PrincipalRecord?> ReadDueCreatorAsync(DueWorkHint hint, CancellationToken cancellationToken);
    /// <summary>Reloads only the configured queue-deadline subject after validating the hint.</summary>
    Task<PrincipalRecord> ReadQueueDeadlineCreatorAsync(QueueDeadlineHint hint, CancellationToken cancellationToken);
    /// <summary>Reloads only the configured transfer subject and validates the actual source incarnation.</summary>
    Task<PrincipalRecord> ReadTransferCoordinatorAsync(RemoteTransferCoordinationHint hint, CancellationToken cancellationToken);
    /// <summary>Reads the current persisted chunk-work identity and eligibility.</summary>
    Task<SampleChunkWorkHint?> ReadSampleChunkCurrentAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken);
    /// <summary>Reloads the original chunk creator and requires its exact policy epoch.</summary>
    Task<PrincipalRecord> ReadSampleChunkCreatorAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken);
    /// <summary>Checks current physical ANN authority, enrollment and the next original job request.</summary>
    Task<BackgroundReadResult> PrepareAnnProjectionAsync(SiloAddress target, ProjectionConsumerRef consumer, long generation, Guid nodeId, AnnProjectionJobState? current, BackgroundReadBudget budget, CancellationToken cancellationToken);
    /// <summary>Reloads the original ANN creator after current authority and physical-owner checks.</summary>
    Task<BackgroundReadResult> ReadAnnProjectionCreatorAsync(SiloAddress target, AnnProjectionJobState original, BackgroundReadBudget budget, CancellationToken cancellationToken);
    /// <summary>Validates the real completed result against current physical publication lineage.</summary>
    Task<BackgroundReadResult> ValidateAnnProjectionResultAsync(SiloAddress target, AnnProjectionJobState original, AnnMaintenanceResult result, BackgroundReadBudget budget, CancellationToken cancellationToken);
}
