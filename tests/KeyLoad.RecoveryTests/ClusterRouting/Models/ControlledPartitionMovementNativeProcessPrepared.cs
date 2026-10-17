using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal sealed record ControlledPartitionMovementNativeProcessPrepared(
    ControlledPartitionMovementNativeProcessInput Input,
    ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> Before,
    CommitReceipt Seed, DateTimeOffset RecordedAt,
    CompleteBlobUploadRequest BlobRequest, BlobCommitResult<BlobMetadata> Blob,
    PartitionMovePhaseResult Phase);
