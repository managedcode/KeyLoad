using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>Borrowed physical source owner; only privately signed native read capabilities reach it.</summary>
internal interface INativePartitionMovementCapture
{
    Task<PartitionMovementCaptureHandle> CaptureAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, CancellationToken cancellationToken);

    Task<PartitionMovementPageResult> ReadPageAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, PartitionMovementPageQuery query, CancellationToken cancellationToken);

    Task<PartitionMovePhaseResult> ReleaseAsync(PrincipalRecord principal, PartitionMovePeerEnvelope verified,
        Guid handleId, CancellationToken cancellationToken);
}
