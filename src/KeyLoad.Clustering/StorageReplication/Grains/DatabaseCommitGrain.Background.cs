using KeyLoad.Core;
using KeyLoad.Core.Messaging;
using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

public sealed partial class DatabaseCommitGrain
{
    /// <inheritdoc />
    public Task<DueWorkDiscoveryResult> ReadDueWorkPageAsync(DueSweepCursor? cursor, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadDueWorkPageAsync(cursor, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<QueueDeadlineDiscoveryResult> ReadQueueDeadlinePageAsync(QueueDeadlineCursor? cursor, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadQueueDeadlinePageAsync(cursor, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<RemoteTransferPendingPage?> ReadQueueTransferPendingAsync(RemoteTransferPendingCursor? cursor, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadQueueTransferPendingAsync(cursor, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<SampleChunkWorkPage> ReadSampleChunkWorkPageAsync(byte[]? after, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadSampleChunkWorkPageAsync(after, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ReadAnnProjectionWorkPageAsync(SiloAddress target, byte[]? after, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(target, backend => backend.ReadAnnProjectionWorkPageAsync(target, after, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord?> ReadDueCreatorAsync(DueWorkHint hint, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadDueCreatorAsync(hint, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadQueueDeadlineCreatorAsync(QueueDeadlineHint hint, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadQueueDeadlineCreatorAsync(hint, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadTransferCoordinatorAsync(RemoteTransferCoordinationHint hint, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadTransferCoordinatorAsync(hint, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<SampleChunkWorkHint?> ReadSampleChunkCurrentAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadSampleChunkCurrentAsync(hint, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadSampleChunkCreatorAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(null, target => target.ReadSampleChunkCreatorAsync(hint, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> PrepareAnnProjectionAsync(SiloAddress target, ProjectionConsumerRef consumer, long generation, Guid nodeId, AnnProjectionJobState? current, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(target, backend => backend.PrepareAnnProjectionAsync(target, consumer, generation, nodeId, current, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ReadAnnProjectionCreatorAsync(SiloAddress target, AnnProjectionJobState original, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(target, backend => backend.ReadAnnProjectionCreatorAsync(target, original, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ValidateAnnProjectionResultAsync(SiloAddress target, AnnProjectionJobState original, AnnMaintenanceResult result, CancellationToken cancellationToken)
        => ExecuteBackgroundAsync(target, backend => backend.ValidateAnnProjectionResultAsync(target, original, result, cancellationToken), cancellationToken);

}
