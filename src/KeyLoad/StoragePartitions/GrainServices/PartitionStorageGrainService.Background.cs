using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Messaging;
using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Server;

internal sealed partial class PartitionStorageGrainService
{
    /// <inheritdoc />
    public Task<DueWorkDiscoveryResult> ReadDueWorkPageAsync(DueSweepCursor? cursor, CancellationToken cancellationToken)
        => BackgroundTarget.ReadDueWorkPageAsync(cursor, cancellationToken);

    /// <inheritdoc />
    public Task<QueueDeadlineDiscoveryResult> ReadQueueDeadlinePageAsync(QueueDeadlineCursor? cursor, CancellationToken cancellationToken)
        => BackgroundTarget.ReadQueueDeadlinePageAsync(cursor, cancellationToken);

    /// <inheritdoc />
    public Task<RemoteTransferPendingPage?> ReadQueueTransferPendingAsync(RemoteTransferPendingCursor? cursor, CancellationToken cancellationToken)
        => BackgroundTarget.ReadQueueTransferPendingAsync(cursor, cancellationToken);

    /// <inheritdoc />
    public Task<SampleChunkWorkPage> ReadSampleChunkWorkPageAsync(byte[]? after, CancellationToken cancellationToken)
        => BackgroundTarget.ReadSampleChunkWorkPageAsync(after, cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ReadAnnProjectionWorkPageAsync(SiloAddress target, byte[]? after, CancellationToken cancellationToken)
        => BackgroundTarget.ReadAnnProjectionWorkPageAsync(target, after, cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord?> ReadDueCreatorAsync(DueWorkHint hint, CancellationToken cancellationToken)
        => BackgroundTarget.ReadDueCreatorAsync(hint, cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadQueueDeadlineCreatorAsync(QueueDeadlineHint hint, CancellationToken cancellationToken)
        => BackgroundTarget.ReadQueueDeadlineCreatorAsync(hint, cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadTransferCoordinatorAsync(RemoteTransferCoordinationHint hint, CancellationToken cancellationToken)
        => BackgroundTarget.ReadTransferCoordinatorAsync(hint, cancellationToken);

    /// <inheritdoc />
    public Task<SampleChunkWorkHint?> ReadSampleChunkCurrentAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
        => BackgroundTarget.ReadSampleChunkCurrentAsync(hint, cancellationToken);

    /// <inheritdoc />
    public Task<PrincipalRecord> ReadSampleChunkCreatorAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
        => BackgroundTarget.ReadSampleChunkCreatorAsync(hint, cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> PrepareAnnProjectionAsync(SiloAddress target, ProjectionConsumerRef consumer, long generation, Guid nodeId, AnnProjectionJobState? current, CancellationToken cancellationToken)
        => BackgroundTarget.PrepareAnnProjectionAsync(target, consumer, generation, nodeId, current, cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ReadAnnProjectionCreatorAsync(SiloAddress target, AnnProjectionJobState original, CancellationToken cancellationToken)
        => BackgroundTarget.ReadAnnProjectionCreatorAsync(target, original, cancellationToken);

    /// <inheritdoc />
    public Task<BackgroundReadResult> ValidateAnnProjectionResultAsync(SiloAddress target, AnnProjectionJobState original, AnnMaintenanceResult result, CancellationToken cancellationToken)
        => BackgroundTarget.ValidateAnnProjectionResultAsync(target, original, result, cancellationToken);

}
